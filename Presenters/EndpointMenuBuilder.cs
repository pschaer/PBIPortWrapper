using System;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Builds the tray's XMLA endpoint section (#125): what the endpoint is doing, the
    /// switch that turns it on, how callers authenticate, and the two conditions a
    /// user cannot otherwise discover.
    ///
    /// Settings are written through <see cref="ConfigService"/>, never applied here —
    /// <see cref="XmlaEndpointCoordinator"/> owns the listener and reacts to the
    /// configuration change. The one exception is Restart, which is an action rather
    /// than a setting: nothing changes, the same settings are retried.
    ///
    /// Split out of TrayMenuManager purely for the 250-line limit.
    /// </summary>
    public class EndpointMenuBuilder
    {
        private readonly XmlaEndpointCoordinator _endpoint;
        private readonly ConfigService _config;

        public EndpointMenuBuilder(XmlaEndpointCoordinator endpoint, ConfigService config)
        {
            _endpoint = endpoint;
            _config = config;
        }

        /// <summary>The endpoint section, as one collapsible item. UI thread only.</summary>
        public ToolStripMenuItem Build()
        {
            EndpointStatus status = _endpoint?.Status;
            if (status == null) return new ToolStripMenuItem("XMLA endpoint") { Enabled = false };

            // The status rides on the top-level label: a warning inside a submenu is a
            // warning nobody reads, and "this machine only" is exactly what a user needs
            // to see without hunting for it.
            var item = new ToolStripMenuItem($"XMLA endpoint  —  {status.Summary}");

            item.DropDownItems.Add(new ToolStripMenuItem("Enabled", null,
                (s, e) => _config?.SetEndpointEnabled(!status.Enabled))
            {
                Checked = status.Enabled,
                ToolTipText = "Serve models over HTTP so other machines can query them."
            });

            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(BuildAuthMenu(status));

            AddWarnings(item, status);

            if (status.Enabled)
            {
                item.DropDownItems.Add(new ToolStripSeparator());
                item.DropDownItems.Add(new ToolStripMenuItem("Restart endpoint", null,
                    (s, e) => _endpoint?.Restart())
                {
                    ToolTipText = "Retry with the current settings — after freeing the port, for instance."
                });
            }

            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(new ToolStripMenuItem("Port and host name are in the dashboard") { Enabled = false });

            return item;
        }

        private ToolStripMenuItem BuildAuthMenu(EndpointStatus status)
        {
            var menu = new ToolStripMenuItem("Authentication");
            foreach (BridgeAuthMode mode in BridgeAuthModeLabel.Order)
            {
                BridgeAuthMode captured = mode;
                menu.DropDownItems.Add(new ToolStripMenuItem(BridgeAuthModeLabel.For(mode), null,
                    (s, e) => _config?.SetEndpointAuthMode(captured))
                {
                    Checked = status.AuthMode == mode,
                    ToolTipText = BridgeAuthModeLabel.Describe(mode)
                });
            }
            return menu;
        }

        /// <summary>
        /// The two states that look like success and are not: running where nobody can
        /// reach it, and running where anybody can.
        /// </summary>
        private void AddWarnings(ToolStripMenuItem item, EndpointStatus status)
        {
            if (status.Running && status.IsLocalOnly)
            {
                item.DropDownItems.Add(new ToolStripSeparator());
                item.DropDownItems.Add(new ToolStripMenuItem(
                    "Not reachable from other machines") { Enabled = false });

                // The fix is a one-time elevated command, and the failure it cures is
                // silent — the endpoint looks healthy while no remote client connects.
                item.DropDownItems.Add(new ToolStripMenuItem("Copy the command that fixes this", null,
                    (s, e) => CopyToClipboard(EndpointUrlBuilder.UrlAclCommand(status.Port)))
                {
                    ToolTipText = "Run it once in an elevated PowerShell, then restart the endpoint."
                });
            }

            if (status.IsUnauthenticated)
            {
                item.DropDownItems.Add(new ToolStripSeparator());
                item.DropDownItems.Add(new ToolStripMenuItem(
                    "Anyone on this network can query and change models") { Enabled = false });
            }
        }

        internal static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch { /* clipboard can transiently fail; ignore */ }
        }
    }
}
