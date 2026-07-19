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

            // #9: config rules match by file name, so an unsaved model would
            // orphan its rule on the first real save - block configuration and
            // say why instead of silently not persisting.
            if (string.Equals(row.Cells["colModelName"].Value?.ToString(), "Untitled", StringComparison.OrdinalIgnoreCase))
            {
                _setRowStatus(row, "Unsaved", Color.Gray, "", true);
                row.Cells["colStatus"].ToolTipText = "Save the .pbix in Power BI Desktop to configure this instance.";
                row.Cells["colServe"].Value = "";
                row.Cells["colActive"].Value = "";
                return;
            }

            var workspaceId = row.Tag as string;
            var session = workspaceId != null ? _sessionLookup(workspaceId) : null;
            if (session != null)
            {
                // The serve session owns the proxy: plain forwarding controls stay
                // blank until Stop Serving hands the row back.
                _setRowStatus(row, "Serving", Color.MediumBlue, "", true);
                row.Cells["colServe"].Value = "Stop Serving";
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
                _setRowStatus(row, "Running", Color.Green, "Stop", true);
                row.Cells["colActive"].Value = _proxyManager.GetActiveConnections(port);
            }
            else
            {
                _setRowStatus(row, "Ready", Color.Black, port > 0 ? "Start" : "Set Port", false);
                row.Cells["colActive"].Value = "";
            }

            var rule = _ruleLookup(row.Cells["colModelName"].Value?.ToString());
            bool canServe = rule != null
                && AliasValidator.ValidateAlias(rule.StableAlias).IsValid
                && rule.FixedPort >= 1024 && rule.FixedPort <= 65535;
            row.Cells["colServe"].Value = canServe ? "Serve" : "";
        }

        public void PaintOffline(DataGridViewRow row)
        {
            _setRowStatus(row, "Offline", Color.Gray, "Remove", false);
            row.Cells["colServe"].Value = "";
            row.Cells["colActive"].Value = "";
        }
    }
}
