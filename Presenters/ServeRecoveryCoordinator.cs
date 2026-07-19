using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Startup crash recovery (#58). While recovery records exist in config, each
    /// instance snapshot triggers a background match (by immutable database ID);
    /// stale records are cleared inside Core, and each live match prompts the user
    /// once: resume serving or restore the original database name. A snapshot with
    /// no records pending is a cheap no-op, so normal starts pay nothing.
    /// </summary>
    public class ServeRecoveryCoordinator
    {
        private readonly ServeSessionService _sessions;
        private readonly IDatabaseIdResolver _idResolver;
        private readonly ConfigService _configService;
        private readonly Control _uiMarshal;
        private readonly Action<string> _log;

        private readonly HashSet<string> _prompted = new HashSet<string>();
        private bool _checkRunning;

        public ServeRecoveryCoordinator(
            ServeSessionService sessions,
            IDatabaseIdResolver idResolver,
            ConfigService configService,
            Control uiMarshal,
            Action<string> log)
        {
            _sessions = sessions;
            _idResolver = idResolver;
            _configService = configService;
            _uiMarshal = uiMarshal;
            _log = log;
        }

        /// <summary>Call with every snapshot (any thread). Prompts arrive on the UI thread.</summary>
        public void OnSnapshot(IReadOnlyList<PowerBIInstance> instances)
        {
            var records = _configService.Current?.ServeRecoveryRecords;
            if (records == null || records.Count == 0) return;
            if (_checkRunning) return;
            _checkRunning = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    var candidates = await _sessions.CheckRecoveryAsync(instances, _idResolver);
                    foreach (var candidate in candidates)
                    {
                        if (!_prompted.Add(candidate.Record.WorkspaceId)) continue;
                        var c = candidate;
                        _uiMarshal.BeginInvoke(new Action(() => Prompt(c)));
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Serve recovery check failed: {ex.Message}");
                }
                finally
                {
                    _checkRunning = false;
                }
            });
        }

        private async void Prompt(ServeRecoveryCandidate candidate)
        {
            var profile = _configService.FindRule(candidate.Instance.FileName);
            bool canResume = profile != null
                && !string.IsNullOrEmpty(profile.StableAlias)
                && profile.FixedPort >= 1024 && profile.FixedPort <= 65535;

            ServeResult result;
            if (canResume)
            {
                var choice = MessageBox.Show(
                    $"The wrapper closed while serving '{candidate.Record.Alias}' ({candidate.Instance.FileName}), " +
                    "and Power BI Desktop is still running with the renamed database.\n\n" +
                    $"Yes – resume serving on port {profile.FixedPort}\n" +
                    "No – restore the original database name\n" +
                    "Cancel – decide later",
                    "Serve session recovery", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (choice == DialogResult.Cancel) return;
                result = choice == DialogResult.Yes
                    ? await _sessions.ResumeServingAsync(candidate, profile)
                    : await _sessions.RestoreOriginalNameAsync(candidate);
            }
            else
            {
                var choice = MessageBox.Show(
                    $"The wrapper closed while serving '{candidate.Record.Alias}' ({candidate.Instance.FileName}), " +
                    "and Power BI Desktop is still running with the renamed database. " +
                    "No serve profile exists for this model anymore.\n\n" +
                    "Restore the original database name now?",
                    "Serve session recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (choice != DialogResult.Yes) return;
                result = await _sessions.RestoreOriginalNameAsync(candidate);
            }

            _log?.Invoke(result.Message);
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Serve session recovery",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Allow another attempt on a later snapshot.
                _prompted.Remove(candidate.Record.WorkspaceId);
            }
        }
    }
}
