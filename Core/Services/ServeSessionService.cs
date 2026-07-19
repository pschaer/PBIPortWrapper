using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    public enum ServeEndReason
    {
        Stopped,
        DesktopClosed
    }

    /// <summary>
    /// A recovery record matched to a live instance (#58): the wrapper crashed
    /// mid-serve and Desktop is still running with the database (possibly) renamed.
    /// The UI offers "restore original name" or "resume serving".
    /// </summary>
    public class ServeRecoveryCandidate
    {
        public ServeRecoveryRecord Record { get; }
        public PowerBIInstance Instance { get; }

        public ServeRecoveryCandidate(ServeRecoveryRecord record, PowerBIInstance instance)
        {
            Record = record;
            Instance = instance;
        }
    }

    public class ServeSessionEventArgs : EventArgs
    {
        public ServeSession Session { get; }
        public ServeEndReason? Reason { get; }

        public ServeSessionEventArgs(ServeSession session, ServeEndReason? reason = null)
        {
            Session = session;
            Reason = reason;
        }
    }

    public class ServeResult
    {
        public bool Success { get; private set; }

        /// <summary>
        /// True when preflight could not prove the model is clean and the caller did
        /// not pass userConfirmedSaved: the UI must ask the user and call again.
        /// </summary>
        public bool NeedsConfirmation { get; private set; }

        public DirtyState DirtyState { get; private set; }
        public string Message { get; private set; }

        public static ServeResult Ok(string message = null) =>
            new ServeResult { Success = true, Message = message };

        public static ServeResult Fail(string message) =>
            new ServeResult { Success = false, Message = message };

        public static ServeResult Confirm(DirtyState state) => new ServeResult
        {
            Success = false,
            NeedsConfirmation = true,
            DirtyState = state,
            Message = "Cannot verify that the model has been saved. Confirm before serving."
        };
    }

    /// <summary>
    /// The serve-only session state machine from serving-workflow.md (#57):
    /// preflight → persist recovery record → rename DB to alias → start proxy, and
    /// the two exits (user Stop restores the name; Desktop closing needs no restore,
    /// per experiment E5). Holds active sessions in memory; the recovery record in
    /// config outlives a wrapper crash and is consumed by startup recovery (#58).
    /// </summary>
    public class ServeSessionService
    {
        private readonly IRenameEngine _renameEngine;
        private readonly ProxyManager _proxyManager;
        private readonly ConfigService _configService;
        private readonly IDirtyStateProbe _dirtyProbe;
        private readonly ILogger _logger;

        private readonly Dictionary<string, ServeSession> _sessions = new Dictionary<string, ServeSession>();
        private readonly object _sessionLock = new object();

        public event EventHandler<ServeSessionEventArgs> SessionStarted;
        public event EventHandler<ServeSessionEventArgs> SessionEnded;

        public ServeSessionService(
            IRenameEngine renameEngine,
            ProxyManager proxyManager,
            ConfigService configService,
            IDirtyStateProbe dirtyProbe = null,
            ILogger logger = null)
        {
            _renameEngine = renameEngine;
            _proxyManager = proxyManager;
            _configService = configService;
            _dirtyProbe = dirtyProbe ?? new NullDirtyStateProbe();
            _logger = logger;
        }

        public IReadOnlyList<ServeSession> ActiveSessions
        {
            get { lock (_sessionLock) return _sessions.Values.ToList(); }
        }

        public ServeSession FindSession(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return null;
            lock (_sessionLock) return _sessions.TryGetValue(workspaceId, out var s) ? s : null;
        }

        public async Task<ServeResult> StartServingAsync(
            PowerBIInstance instance, PortMappingRule profile, bool userConfirmedSaved = false)
        {
            if (instance == null || profile == null)
                return ServeResult.Fail("Instance and profile are required.");
            if (string.IsNullOrEmpty(instance.WorkspaceId))
                return ServeResult.Fail("Instance has no workspace identity.");
            if (string.IsNullOrEmpty(instance.DatabaseName))
                return ServeResult.Fail("Instance database name is unknown; cannot record a recovery anchor.");

            var (aliasValid, aliasError) = AliasValidator.ValidateAlias(profile.StableAlias);
            if (!aliasValid)
                return ServeResult.Fail($"Profile has no usable alias: {aliasError}");
            if (profile.FixedPort < 1024 || profile.FixedPort > 65535)
                return ServeResult.Fail($"Invalid profile port {profile.FixedPort}.");

            if (FindSession(instance.WorkspaceId) != null)
                return ServeResult.Fail($"Already serving {instance.FileName}.");

            var existingTarget = _proxyManager.GetTargetPort(profile.FixedPort);
            if (existingTarget.HasValue && existingTarget.Value != instance.Port)
                return ServeResult.Fail(
                    $"Port {profile.FixedPort} already forwards to another instance (target {existingTarget.Value}).");

            // Preflight (E3: Desktop errors while renamed; never serve unsaved work).
            var dirty = _dirtyProbe.Probe(instance.ProcessId);
            if (dirty == DirtyState.Dirty)
                return ServeResult.Fail("Desktop has unsaved changes. Save before serving.");
            if (dirty != DirtyState.Clean && !userConfirmedSaved)
                return ServeResult.Confirm(dirty);

            // Recovery record goes to disk BEFORE any mutation — the crash anchor.
            var record = new ServeRecoveryRecord
            {
                DatabaseId = instance.DatabaseName,
                Alias = profile.StableAlias,
                WorkspaceId = instance.WorkspaceId,
                Pid = instance.ProcessId,
                StartedUtc = DateTime.UtcNow
            };
            _configService.AddServeRecoveryRecord(record);

            var rename = await _renameEngine.RenameAsync(instance.Port, instance.DatabaseName, profile.StableAlias);
            if (!rename.Success)
            {
                _configService.RemoveServeRecoveryRecord(instance.WorkspaceId);
                return ServeResult.Fail($"Rename failed: {rename.Message}");
            }

            try
            {
                await _proxyManager.StartProxyAsync(
                    profile.FixedPort, instance.Port, profile.AllowNetworkAccess, instance.FileName);
            }
            catch (Exception ex)
            {
                _logger?.LogError("ServeSession", $"Proxy start failed for {instance.FileName}; rolling back rename", ex);
                var rollback = await _renameEngine.RenameAsync(instance.Port, profile.StableAlias, instance.DatabaseName);
                if (!rollback.Success)
                    _logger?.LogError("ServeSession",
                        $"Rollback rename failed for {instance.FileName}: {rollback.Message}. Recovery record retained.");
                else
                    _configService.RemoveServeRecoveryRecord(instance.WorkspaceId);
                return ServeResult.Fail($"Could not start proxy on port {profile.FixedPort}: {ex.Message}");
            }

            var session = new ServeSession
            {
                WorkspaceId = instance.WorkspaceId,
                FileName = instance.FileName,
                Alias = profile.StableAlias,
                DatabaseId = record.DatabaseId,
                InstancePort = instance.Port,
                FixedPort = profile.FixedPort,
                Pid = instance.ProcessId,
                StartedUtc = record.StartedUtc
            };
            lock (_sessionLock) _sessions[session.WorkspaceId] = session;

            _logger?.LogInfo("ServeSession",
                $"Serving {session.FileName} as '{session.Alias}' on port {session.FixedPort} (db {session.DatabaseId})");
            SessionStarted?.Invoke(this, new ServeSessionEventArgs(session));
            return ServeResult.Ok($"Serving '{session.Alias}' on port {session.FixedPort}.");
        }

        /// <summary>
        /// User Stop (E2: graceful): rename back to the original GUID, stop the
        /// proxy, clear the record. Desktop stays usable afterwards.
        /// </summary>
        public async Task<ServeResult> StopServingAsync(string workspaceId)
        {
            var session = FindSession(workspaceId);
            if (session == null)
                return ServeResult.Fail("No active serve session for this instance.");

            var rename = await _renameEngine.RenameAsync(session.InstancePort, session.Alias, session.DatabaseId);

            EndSession(session, ServeEndReason.Stopped);

            if (!rename.Success)
            {
                // Most likely the instance died mid-stop (then there is nothing to
                // restore, per E5). If Desktop is still alive it is left renamed —
                // surface that loudly.
                _logger?.LogError("ServeSession",
                    $"Rename-back failed for {session.FileName}: {rename.Message}");
                return ServeResult.Ok(
                    $"Serve session ended, but restoring the database name failed: {rename.Message} " +
                    "If Power BI Desktop is still open, close it without saving and reopen.");
            }

            return ServeResult.Ok($"Stopped serving '{session.Alias}'; database name restored.");
        }

        /// <summary>
        /// Snapshot hook: a serving instance that disappeared means Desktop closed —
        /// stop the proxy and clear the record; nothing to restore (E5).
        /// </summary>
        public void OnInstancesChanged(IReadOnlyList<PowerBIInstance> instances)
        {
            if (instances == null) return;

            List<ServeSession> gone;
            lock (_sessionLock)
            {
                gone = _sessions.Values
                    .Where(s => !instances.Any(i => i.WorkspaceId == s.WorkspaceId))
                    .ToList();
            }

            foreach (var session in gone)
            {
                _logger?.LogInfo("ServeSession",
                    $"Desktop closed while serving {session.FileName}; cleaning up session");
                EndSession(session, ServeEndReason.DesktopClosed);
            }
        }

        /// <summary>
        /// Startup recovery (#58): matches persisted recovery records against the
        /// live snapshot by database ID (immutable through rename, E2). Records
        /// whose database is found on no queryable instance are cleared silently —
        /// Desktop is gone and a reopen is clean (E5). Records that could not be
        /// decided (an instance failed to answer the ID query) are left untouched
        /// for a later call. Requires an <see cref="IDatabaseIdResolver"/>.
        /// </summary>
        public async Task<IReadOnlyList<ServeRecoveryCandidate>> CheckRecoveryAsync(
            IReadOnlyList<PowerBIInstance> instances, IDatabaseIdResolver idResolver)
        {
            var candidates = new List<ServeRecoveryCandidate>();
            var records = _configService.Current?.ServeRecoveryRecords?.ToList();
            if (records == null || records.Count == 0 || idResolver == null) return candidates;
            instances = instances ?? Array.Empty<PowerBIInstance>();

            // One ID query per instance, shared across all records.
            var idsByPort = new Dictionary<int, string>();
            foreach (var instance in instances)
                idsByPort[instance.Port] = await idResolver.GetDatabaseIdAsync(instance.Port);
            bool allResolved = idsByPort.Values.All(id => id != null);

            foreach (var record in records)
            {
                // A session this service already tracks needs no recovery.
                if (FindSession(record.WorkspaceId) != null) continue;

                var match = instances.FirstOrDefault(i =>
                    idsByPort.TryGetValue(i.Port, out var id) && id == record.DatabaseId);

                if (match != null)
                {
                    candidates.Add(new ServeRecoveryCandidate(record, match));
                }
                else if (allResolved)
                {
                    _logger?.LogInfo("ServeSession",
                        $"Clearing stale recovery record for '{record.Alias}' (db {record.DatabaseId}): no live instance has this database");
                    _configService.RemoveServeRecoveryRecord(record.WorkspaceId);
                }
                // else: an instance did not answer — undecided, keep the record.
            }

            return candidates;
        }

        /// <summary>
        /// Recovery choice "restore original name": rename the database back to the
        /// recorded GUID and clear the record. If the crash happened before the
        /// rename, the engine reports "already has this name" and this still
        /// succeeds — the record is cleared either way on success.
        /// </summary>
        public async Task<ServeResult> RestoreOriginalNameAsync(ServeRecoveryCandidate candidate)
        {
            if (candidate == null) return ServeResult.Fail("Nothing to restore.");

            var rename = await _renameEngine.RenameAsync(
                candidate.Instance.Port, candidate.Instance.DatabaseName, candidate.Record.DatabaseId);
            if (!rename.Success)
                return ServeResult.Fail($"Could not restore database name: {rename.Message}");

            _configService.RemoveServeRecoveryRecord(candidate.Record.WorkspaceId);
            _logger?.LogInfo("ServeSession",
                $"Recovery: restored database name for {candidate.Instance.FileName} (db {candidate.Record.DatabaseId})");
            return ServeResult.Ok("Database name restored.");
        }

        /// <summary>
        /// Recovery choice "resume serving": re-register the session and restart the
        /// proxy. If the crash interrupted the serve before the rename took effect,
        /// the rename to the alias is completed first. The recovery record is kept —
        /// it is once again the crash anchor of a live session.
        /// </summary>
        public async Task<ServeResult> ResumeServingAsync(ServeRecoveryCandidate candidate, PortMappingRule profile)
        {
            if (candidate == null || profile == null)
                return ServeResult.Fail("Candidate and profile are required.");
            if (profile.FixedPort < 1024 || profile.FixedPort > 65535)
                return ServeResult.Fail($"Invalid profile port {profile.FixedPort}.");
            if (FindSession(candidate.Record.WorkspaceId) != null)
                return ServeResult.Fail($"Already serving {candidate.Instance.FileName}.");

            var existingTarget = _proxyManager.GetTargetPort(profile.FixedPort);
            if (existingTarget.HasValue && existingTarget.Value != candidate.Instance.Port)
                return ServeResult.Fail(
                    $"Port {profile.FixedPort} already forwards to another instance (target {existingTarget.Value}).");

            if (!string.Equals(candidate.Instance.DatabaseName, candidate.Record.Alias, StringComparison.OrdinalIgnoreCase))
            {
                var rename = await _renameEngine.RenameAsync(
                    candidate.Instance.Port, candidate.Instance.DatabaseName, candidate.Record.Alias);
                if (!rename.Success)
                    return ServeResult.Fail($"Could not re-apply alias: {rename.Message}");
            }

            try
            {
                await _proxyManager.StartProxyAsync(
                    profile.FixedPort, candidate.Instance.Port, profile.AllowNetworkAccess, candidate.Instance.FileName);
            }
            catch (Exception ex)
            {
                return ServeResult.Fail($"Could not start proxy on port {profile.FixedPort}: {ex.Message}");
            }

            var session = new ServeSession
            {
                WorkspaceId = candidate.Record.WorkspaceId,
                FileName = candidate.Instance.FileName,
                Alias = candidate.Record.Alias,
                DatabaseId = candidate.Record.DatabaseId,
                InstancePort = candidate.Instance.Port,
                FixedPort = profile.FixedPort,
                Pid = candidate.Instance.ProcessId,
                StartedUtc = candidate.Record.StartedUtc
            };
            lock (_sessionLock) _sessions[session.WorkspaceId] = session;

            // The record predates this process; re-key it to the live instance's pid
            // so a second crash still recovers against current reality.
            _configService.AddServeRecoveryRecord(new ServeRecoveryRecord
            {
                DatabaseId = session.DatabaseId,
                Alias = session.Alias,
                WorkspaceId = session.WorkspaceId,
                Pid = session.Pid,
                StartedUtc = session.StartedUtc
            });

            _logger?.LogInfo("ServeSession",
                $"Recovery: resumed serving {session.FileName} as '{session.Alias}' on port {session.FixedPort}");
            SessionStarted?.Invoke(this, new ServeSessionEventArgs(session));
            return ServeResult.Ok($"Resumed serving '{session.Alias}' on port {session.FixedPort}.");
        }

        private void EndSession(ServeSession session, ServeEndReason reason)
        {
            _proxyManager.StopProxy(session.FixedPort);
            _configService.RemoveServeRecoveryRecord(session.WorkspaceId);
            lock (_sessionLock) _sessions.Remove(session.WorkspaceId);
            SessionEnded?.Invoke(this, new ServeSessionEventArgs(session, reason));
        }
    }
}
