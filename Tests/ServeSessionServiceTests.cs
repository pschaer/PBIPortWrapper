using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the serve-session state machine (#57) through the IRenameEngine and
    /// IDirtyStateProbe seams: recovery record before mutation, rename/proxy
    /// ordering, both session exits, and the failure rollbacks. Live-instance
    /// behavior is the E2E increment (#60).
    /// </summary>
    public sealed class ServeSessionServiceTests : IDisposable
    {
        private sealed class FakeRenameEngine : IRenameEngine
        {
            public List<(int Port, string From, string To)> Calls { get; } = new List<(int, string, string)>();
            public Func<string, bool> ShouldFail { get; set; } = _ => false;
            public Action OnRename { get; set; }

            public Task<RenameResult> RenameAsync(int port, string currentDbName, string newName)
            {
                Calls.Add((port, currentDbName, newName));
                OnRename?.Invoke();
                return Task.FromResult(ShouldFail(newName)
                    ? RenameResult.Fail("simulated failure")
                    : RenameResult.Ok("ok"));
            }
        }

        private sealed class FakeDirtyProbe : IDirtyStateProbe
        {
            public DirtyState State { get; set; } = DirtyState.Clean;
            public DirtyState Probe(int processId) => State;
        }

        private readonly string _tempDir;
        private readonly ConfigService _config;
        private readonly ProxyManager _proxyManager = new ProxyManager();
        private readonly FakeRenameEngine _engine = new FakeRenameEngine();
        private readonly FakeDirtyProbe _probe = new FakeDirtyProbe();
        private readonly ServeSessionService _service;

        public ServeSessionServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PBIPortWrapperTests", Guid.NewGuid().ToString("N"));
            _config = new ConfigService(new ConfigurationManager(_tempDir));
            _config.Load();
            _service = new ServeSessionService(_engine, _proxyManager, _config, _probe);
        }

        public void Dispose()
        {
            _proxyManager.StopAll();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static PowerBIInstance Instance(int port) => new PowerBIInstance
        {
            WorkspaceId = "ws-1",
            FileName = "Sales",
            DatabaseName = "9f8e7d6c-0000-0000-0000-000000000001",
            Port = port,
            ProcessId = 4242
        };

        private static PortMappingRule Profile(int fixedPort) => new PortMappingRule
        {
            ModelNamePattern = "Sales",
            FixedPort = fixedPort,
            StableAlias = "Sales",
            AllowNetworkAccess = false
        };

        private ConfigService ReloadFromDisk()
        {
            var fresh = new ConfigService(new ConfigurationManager(_tempDir));
            fresh.Load();
            return fresh;
        }

        [Fact]
        public async Task StartServing_RenamesPersistsAndStartsProxy()
        {
            var instance = Instance(FreePort());
            var profile = Profile(FreePort());
            ServeSessionEventArgs started = null;
            _service.SessionStarted += (s, e) => started = e;

            var result = await _service.StartServingAsync(instance, profile);

            Assert.True(result.Success, result.Message);
            var call = Assert.Single(_engine.Calls);
            Assert.Equal((instance.Port, instance.DatabaseName, "Sales"), call);
            Assert.True(_proxyManager.IsRunning(profile.FixedPort));

            var record = Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Equal(instance.DatabaseName, record.DatabaseId);
            Assert.Equal("Sales", record.Alias);
            Assert.Equal("ws-1", record.WorkspaceId);
            Assert.Equal(4242, record.Pid);

            Assert.NotNull(started);
            Assert.Equal("ws-1", started.Session.WorkspaceId);
            Assert.NotNull(_service.FindSession("ws-1"));
        }

        [Fact]
        public async Task RecoveryRecord_IsOnDiskBeforeRenameRuns()
        {
            bool recordExistedAtRenameTime = false;
            _engine.OnRename = () =>
                recordExistedAtRenameTime = ReloadFromDisk().Current.ServeRecoveryRecords.Any();

            await _service.StartServingAsync(Instance(FreePort()), Profile(FreePort()));

            Assert.True(recordExistedAtRenameTime);
        }

        [Fact]
        public async Task Preflight_UnknownWithoutConfirmation_MutatesNothing()
        {
            _probe.State = DirtyState.Unknown;
            var profile = Profile(FreePort());

            var result = await _service.StartServingAsync(Instance(FreePort()), profile);

            Assert.False(result.Success);
            Assert.True(result.NeedsConfirmation);
            Assert.Equal(DirtyState.Unknown, result.DirtyState);
            Assert.Empty(_engine.Calls);
            Assert.False(_proxyManager.IsRunning(profile.FixedPort));
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task Preflight_UnknownWithConfirmation_Proceeds()
        {
            _probe.State = DirtyState.Unknown;

            var result = await _service.StartServingAsync(
                Instance(FreePort()), Profile(FreePort()), userConfirmedSaved: true);

            Assert.True(result.Success, result.Message);
        }

        [Fact]
        public async Task Preflight_Dirty_RefusesEvenWithConfirmation()
        {
            _probe.State = DirtyState.Dirty;

            var result = await _service.StartServingAsync(
                Instance(FreePort()), Profile(FreePort()), userConfirmedSaved: true);

            Assert.False(result.Success);
            Assert.False(result.NeedsConfirmation);
            Assert.Empty(_engine.Calls);
        }

        [Fact]
        public async Task RenameFailure_ClearsRecordAndStartsNoProxy()
        {
            _engine.ShouldFail = name => name == "Sales";
            var profile = Profile(FreePort());

            var result = await _service.StartServingAsync(Instance(FreePort()), profile);

            Assert.False(result.Success);
            Assert.False(_proxyManager.IsRunning(profile.FixedPort));
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Null(_service.FindSession("ws-1"));
        }

        [Fact]
        public async Task ProxyFailure_RollsBackRenameAndClearsRecord()
        {
            var instance = Instance(FreePort());
            int blockedPort = FreePort();
            var blocker = new TcpListener(IPAddress.Loopback, blockedPort);
            blocker.Start();
            try
            {
                var result = await _service.StartServingAsync(instance, Profile(blockedPort));

                Assert.False(result.Success);
                Assert.Equal(2, _engine.Calls.Count);
                Assert.Equal((instance.Port, "Sales", instance.DatabaseName), _engine.Calls[1]);
                Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
                Assert.Null(_service.FindSession("ws-1"));
            }
            finally
            {
                blocker.Stop();
            }
        }

        [Fact]
        public async Task StopServing_RenamesBackStopsProxyClearsRecord()
        {
            var instance = Instance(FreePort());
            var profile = Profile(FreePort());
            await _service.StartServingAsync(instance, profile);
            ServeSessionEventArgs ended = null;
            _service.SessionEnded += (s, e) => ended = e;

            var result = await _service.StopServingAsync("ws-1");

            Assert.True(result.Success, result.Message);
            Assert.Equal((instance.Port, "Sales", instance.DatabaseName), _engine.Calls[1]);
            Assert.False(_proxyManager.IsRunning(profile.FixedPort));
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Null(_service.FindSession("ws-1"));
            Assert.Equal(ServeEndReason.Stopped, ended?.Reason);
        }

        [Fact]
        public async Task DesktopClosed_CleansUpWithoutRename()
        {
            var instance = Instance(FreePort());
            var profile = Profile(FreePort());
            await _service.StartServingAsync(instance, profile);
            int renamesAfterStart = _engine.Calls.Count;
            ServeSessionEventArgs ended = null;
            _service.SessionEnded += (s, e) => ended = e;

            _service.OnInstancesChanged(Array.Empty<PowerBIInstance>());

            Assert.Equal(renamesAfterStart, _engine.Calls.Count);
            Assert.False(_proxyManager.IsRunning(profile.FixedPort));
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Null(_service.FindSession("ws-1"));
            Assert.Equal(ServeEndReason.DesktopClosed, ended?.Reason);
        }

        [Fact]
        public async Task OnInstancesChanged_IgnoresSessionsStillPresent()
        {
            var instance = Instance(FreePort());
            var profile = Profile(FreePort());
            await _service.StartServingAsync(instance, profile);

            _service.OnInstancesChanged(new[] { instance });

            Assert.True(_proxyManager.IsRunning(profile.FixedPort));
            Assert.NotNull(_service.FindSession("ws-1"));
        }

        [Fact]
        public async Task StartServing_RefusesProfileWithoutAlias()
        {
            var profile = Profile(FreePort());
            profile.StableAlias = null;

            var result = await _service.StartServingAsync(Instance(FreePort()), profile);

            Assert.False(result.Success);
            Assert.Empty(_engine.Calls);
        }

        [Fact]
        public async Task StartServing_RefusesSecondSessionForSameWorkspace()
        {
            var instance = Instance(FreePort());
            await _service.StartServingAsync(instance, Profile(FreePort()));

            var result = await _service.StartServingAsync(instance, Profile(FreePort()));

            Assert.False(result.Success);
            Assert.Contains("Already serving", result.Message);
        }

        [Fact]
        public async Task StartServing_RefusesPortForwardingAnotherInstance()
        {
            int fixedPort = FreePort();
            int otherTarget = FreePort();
            await _proxyManager.StartProxyAsync(fixedPort, otherTarget, false, "Other");

            var result = await _service.StartServingAsync(Instance(FreePort()), Profile(fixedPort));

            Assert.False(result.Success);
            Assert.Empty(_engine.Calls);
        }

        [Fact]
        public async Task StopServing_WithoutSession_Fails()
        {
            var result = await _service.StopServingAsync("ws-none");

            Assert.False(result.Success);
        }

        // ---- Crash recovery (#58) ----

        private sealed class FakeIdResolver : IDatabaseIdResolver
        {
            public Dictionary<int, string> IdsByPort { get; } = new Dictionary<int, string>();
            public Task<string> GetDatabaseIdAsync(int port) =>
                Task.FromResult(IdsByPort.TryGetValue(port, out var id) ? id : null);
        }

        private ServeRecoveryRecord PersistRecord(string databaseId = "9f8e7d6c-0000-0000-0000-000000000001")
        {
            var record = new ServeRecoveryRecord
            {
                DatabaseId = databaseId,
                Alias = "Sales",
                WorkspaceId = "ws-1",
                Pid = 999,
                StartedUtc = DateTime.UtcNow.AddMinutes(-5)
            };
            _config.AddServeRecoveryRecord(record);
            return record;
        }

        [Fact]
        public async Task CheckRecovery_MatchesLiveInstanceByDatabaseId()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort());
            instance.DatabaseName = "Sales"; // renamed at crash time
            var resolver = new FakeIdResolver();
            resolver.IdsByPort[instance.Port] = record.DatabaseId;

            var candidates = await _service.CheckRecoveryAsync(new[] { instance }, resolver);

            var candidate = Assert.Single(candidates);
            Assert.Same(instance, candidate.Instance);
            Assert.Equal(record.DatabaseId, candidate.Record.DatabaseId);
            // Record stays until the user decides.
            Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task CheckRecovery_ClearsStaleRecordWhenDesktopGone()
        {
            PersistRecord();

            var candidates = await _service.CheckRecoveryAsync(
                Array.Empty<PowerBIInstance>(), new FakeIdResolver());

            Assert.Empty(candidates);
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task CheckRecovery_ClearsRecordWhenLiveInstancesHaveOtherDatabases()
        {
            PersistRecord();
            var other = Instance(FreePort());
            var resolver = new FakeIdResolver();
            resolver.IdsByPort[other.Port] = "some-other-database-id";

            var candidates = await _service.CheckRecoveryAsync(new[] { other }, resolver);

            Assert.Empty(candidates);
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task CheckRecovery_KeepsRecordWhenAnInstanceCannotBeQueried()
        {
            PersistRecord();
            var silent = Instance(FreePort()); // resolver has no entry -> null

            var candidates = await _service.CheckRecoveryAsync(new[] { silent }, new FakeIdResolver());

            Assert.Empty(candidates);
            Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task RestoreOriginalName_RenamesBackAndClearsRecord()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort());
            instance.DatabaseName = "Sales";
            var candidate = new ServeRecoveryCandidate(record, instance);

            var result = await _service.RestoreOriginalNameAsync(candidate);

            Assert.True(result.Success, result.Message);
            var call = Assert.Single(_engine.Calls);
            Assert.Equal((instance.Port, "Sales", record.DatabaseId), call);
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task RestoreOriginalName_KeepsRecordOnRenameFailure()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort());
            instance.DatabaseName = "Sales";
            _engine.ShouldFail = _ => true;

            var result = await _service.RestoreOriginalNameAsync(new ServeRecoveryCandidate(record, instance));

            Assert.False(result.Success);
            Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
        }

        [Fact]
        public async Task ResumeServing_StartsProxyAndRegistersSession_NoRenameWhenAliased()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort());
            instance.DatabaseName = "Sales"; // already renamed
            var profile = Profile(FreePort());
            ServeSessionEventArgs started = null;
            _service.SessionStarted += (s, e) => started = e;

            var result = await _service.ResumeServingAsync(new ServeRecoveryCandidate(record, instance), profile);

            Assert.True(result.Success, result.Message);
            Assert.Empty(_engine.Calls);
            Assert.True(_proxyManager.IsRunning(profile.FixedPort));
            Assert.NotNull(_service.FindSession("ws-1"));
            Assert.NotNull(started);
            // Record stays as the crash anchor of the resumed session, re-keyed to the live pid.
            var persisted = Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Equal(instance.ProcessId, persisted.Pid);
        }

        [Fact]
        public async Task ResumeServing_CompletesInterruptedRename()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort()); // DatabaseName is still the GUID

            var result = await _service.ResumeServingAsync(
                new ServeRecoveryCandidate(record, instance), Profile(FreePort()));

            Assert.True(result.Success, result.Message);
            var call = Assert.Single(_engine.Calls);
            Assert.Equal((instance.Port, instance.DatabaseName, "Sales"), call);
        }

        [Fact]
        public async Task ResumedSession_CleansUpWhenDesktopCloses()
        {
            var record = PersistRecord();
            var instance = Instance(FreePort());
            instance.DatabaseName = "Sales";
            var profile = Profile(FreePort());
            await _service.ResumeServingAsync(new ServeRecoveryCandidate(record, instance), profile);

            _service.OnInstancesChanged(Array.Empty<PowerBIInstance>());

            Assert.False(_proxyManager.IsRunning(profile.FixedPort));
            Assert.Empty(ReloadFromDisk().Current.ServeRecoveryRecords);
            Assert.Null(_service.FindSession("ws-1"));
        }

        [Fact]
        public async Task CheckRecovery_SkipsWorkspacesWithActiveSessions()
        {
            var instance = Instance(FreePort());
            var profile = Profile(FreePort());
            await _service.StartServingAsync(instance, profile); // persists a record for ws-1
            var resolver = new FakeIdResolver();
            resolver.IdsByPort[instance.Port] = instance.DatabaseName;

            var candidates = await _service.CheckRecoveryAsync(new[] { instance }, resolver);

            Assert.Empty(candidates);
            Assert.Single(ReloadFromDisk().Current.ServeRecoveryRecords);
        }
    }
}
