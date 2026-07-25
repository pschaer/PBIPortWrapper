using System;
using System.Drawing;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Single decision point for a row's Status/Action/Serve/Active cells, shared
    /// by the snapshot refresh (GridSyncHelper) and the proxy/serve event paths
    /// (GridPresenter) so a serving row can never be repainted as plain "Running".
    /// </summary>
    public class RowStatusPainter
    {
        private readonly ProxyManager _proxyManager;
        private readonly Func<string, ServeSession> _sessionLookup; // by workspace id
        private readonly Func<string, PortMappingRule> _ruleLookup; // by model name
        private readonly Action<DataGridViewRow, string, Color, string, bool> _setRowStatus;
        private readonly Action<string> _log;

        public RowStatusPainter(
            ProxyManager proxyManager,
            Func<string, ServeSession> sessionLookup,
            Func<string, PortMappingRule> ruleLookup,
            Action<DataGridViewRow, string, Color, string, bool> setRowStatus,
            Action<string> log)
        {
            _proxyManager = proxyManager;
            _sessionLookup = sessionLookup;
            _ruleLookup = ruleLookup;
            _setRowStatus = setRowStatus;
            _log = log;
        }

        /// <summary>Repaints one main grid row from current proxy and serve state.</summary>
        public void Paint(DataGridViewRow row)
        {
            bool live = !string.IsNullOrEmpty(row.Cells["colPbiPort"].Value?.ToString());
            if (!live)
            {
                PaintOffline(row);
                return;
            }

            ProjectPolicy(row);

            // #9: config rules match by file name, so an unsaved model would
            // orphan its rule on the first real save - block configuration and
            // say why instead of silently not persisting.
            if (string.Equals(row.Cells["colModelName"].Value?.ToString(), "Untitled", StringComparison.OrdinalIgnoreCase))
            {
                _setRowStatus(row, "Unsaved", Color.Gray, "", true);
                row.Cells["colStatus"].ToolTipText = "Save the .pbix in Power BI Desktop to configure this instance.";
                row.Cells["colActive"].Value = "";
                return;
            }

            var workspaceId = row.Tag as string;
            var session = workspaceId != null ? _sessionLookup(workspaceId) : null;
            if (session != null)
            {
                // Serving: the single Action menu offers Stop (restores the name).
                _setRowStatus(row, "Serving", Color.MediumBlue, "Actions", true);
                row.Cells["colActive"].Value = _proxyManager.GetActiveConnections(session.FixedPort);
                return;
            }

            int port = 0;
            if (row.Cells["colFixedPort"].Value != null)
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out port);

            bool running = port > 0 && _proxyManager.IsRunning(port);
            if (running)
            {
                // #49: if Desktop restarted within one refresh window, the row was
                // matched by file name and the proxy still forwards to the dead old
                // workspace port. Stop it here; rows with Auto get restarted by
                // ProcessAutoConnect in the same refresh pass, manual rows fall
                // back to Ready.
                int? targetPort = _proxyManager.GetTargetPort(port);
                if (targetPort.HasValue && row.Cells["colPbiPort"].Value is int instancePort && targetPort.Value != instancePort)
                {
                    _proxyManager.StopProxy(port);
                    _log($"Proxy {port} targeted stale port {targetPort.Value}; instance now on {instancePort}. Restarting.");
                    running = false;
                }
            }

            if (running)
            {
                // Forwarding: the Action menu offers Serve and Stop.
                _setRowStatus(row, "Running", Color.Green, "Actions", true);
                row.Cells["colActive"].Value = _proxyManager.GetActiveConnections(port);
            }
            else
            {
                // Off: the Action menu offers Forward and Serve (once a port is set).
                _setRowStatus(row, "Ready", Color.Black, port > 0 ? "Actions" : "Set Port", false);
                row.Cells["colActive"].Value = "";
            }
        }

        public void PaintOffline(DataGridViewRow row)
        {
            ProjectPolicy(row);
            _setRowStatus(row, "Offline", Color.Gray, "Remove", false);
            row.Cells["colActive"].Value = "";
        }

        /// <summary>
        /// Projects the row's config-editable cells that can also change from the tray -
        /// the On-detection dropdown and the Network checkbox - from the rule, so a
        /// change made on the tray (which raises ConfigurationChanged → a repaint, not a
        /// full snapshot) shows up in the grid too (#88). Any externally-changeable cell
        /// must be projected here, not only in the snapshot path (GridSyncHelper). This
        /// is display-only: the user's own edits persist via CurrentCellDirtyStateChanged,
        /// so writing here never loops back into config.
        /// </summary>
        private void ProjectPolicy(DataGridViewRow row)
        {
            var rule = _ruleLookup(row.Cells["colModelName"].Value?.ToString());
            if (rule == null) return; // no profile yet: leave the defaults the grid set

            var grid = row.DataGridView;
            var policyCell = row.Cells["colOnDetection"];
            // Skip the dropdown while it is open/being edited so a pick isn't clobbered.
            if (!(grid != null && grid.IsCurrentCellInEditMode && grid.CurrentCell == policyCell))
            {
                string label = OnDetectionPolicyLabel.For(rule.OnDetection);
                if (policyCell.Value?.ToString() != label) policyCell.Value = label;
            }

            var networkCell = row.Cells["colNetwork"];
            if (!Equals(networkCell.Value, rule.AllowNetworkAccess))
                networkCell.Value = rule.AllowNetworkAccess;
        }
    }
}
