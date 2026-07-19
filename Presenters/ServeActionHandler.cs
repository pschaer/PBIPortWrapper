using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// UI flow for the grid's Serve / Stop Serving actions (#59): confirmation with
    /// the E3-validated warning copy, the dirty-state fallback question when
    /// preflight cannot prove the model is saved, and loud surfacing of a failed
    /// rename-back on stop.
    /// </summary>
    public class ServeActionHandler
    {
        private readonly ServeSessionService _sessions;
        private readonly ConfigService _configService;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;
        private readonly Action<string> _log;

        public ServeActionHandler(
            ServeSessionService sessions,
            ConfigService configService,
            Func<List<PowerBIInstance>> instancesProvider,
            Action<string> log)
        {
            _sessions = sessions;
            _configService = configService;
            _instancesProvider = instancesProvider;
            _log = log;
        }

        public async Task HandleServeAsync(DataGridViewRow row)
        {
            var instance = FindInstance(row);
            if (instance == null)
            {
                MessageBox.Show("Power BI instance not found.", "Serve",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var profile = _configService.FindRule(instance.FileName);
            if (profile == null || string.IsNullOrEmpty(profile.StableAlias))
            {
                MessageBox.Show("No serve profile with an alias exists for this model. " +
                    "Set an alias in the details panel first.", "Serve",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // E3-validated copy: Desktop showing errors while renamed is expected.
            string warning =
                $"Serve '{instance.FileName}' as '{profile.StableAlias}' on port {profile.FixedPort}?\n\n" +
                "While serving, Power BI Desktop will repeatedly show \"Cannot load model\" errors on its own. " +
                "This is expected. Do not troubleshoot in Desktop; click Stop to restore it.";
            if (profile.AllowNetworkAccess)
                warning += $"\n\nNetwork Access is enabled: ensure Windows Firewall allows inbound connections on port {profile.FixedPort}.";

            if (MessageBox.Show(warning, "Start serving",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var result = await _sessions.StartServingAsync(instance, profile);

            if (result.NeedsConfirmation)
            {
                string dirtyHint = result.DirtyState == DirtyState.MaybeDirty
                    ? "The Undo history suggests there may be edits since the model was opened."
                    : "The unsaved-changes state of this model could not be determined.";

                var confirmed = MessageBox.Show(
                    $"{dirtyHint}\n\nServing renames the database while Desktop is running; unsaved work " +
                    "cannot be saved normally until serving stops.\n\nHas the model been saved?",
                    "Confirm model is saved", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirmed != DialogResult.Yes) return;

                result = await _sessions.StartServingAsync(instance, profile, userConfirmedSaved: true);
            }

            _log?.Invoke(result.Message);
            if (!result.Success)
                MessageBox.Show(result.Message, "Serve failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public async Task HandleStopServingAsync(DataGridViewRow row)
        {
            var workspaceId = row.Tag as string;
            if (string.IsNullOrEmpty(workspaceId)) return;

            var result = await _sessions.StopServingAsync(workspaceId);
            _log?.Invoke(result.Message);

            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Stop serving", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (result.Message != null && result.Message.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                // Session ended but the rename-back did not take: Desktop is left
                // renamed and the user must act (close without saving, reopen).
                MessageBox.Show(result.Message, "Stop serving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private PowerBIInstance FindInstance(DataGridViewRow row)
        {
            var workspaceId = row.Tag as string;
            if (string.IsNullOrEmpty(workspaceId)) return null;
            return _instancesProvider().FirstOrDefault(i => i.WorkspaceId == workspaceId);
        }
    }
}
