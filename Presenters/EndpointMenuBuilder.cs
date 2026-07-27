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
        private readonly Func<string> _accessLogPath;

        public EndpointMenuBuilder(
            XmlaEndpointCoordinator endpoint, ConfigService config, Func<string> accessLogPath = null)
        {
            _endpoint = endpoint;
            _config = config;
            _accessLogPath = accessLogPath;
        }

        /// <summary>
        /// The access log (#128): a toggle, and a way to open it. Opening it matters as
        /// much as writing it — a log nobody can find answers nothing.
        /// </summary>
        private ToolStripMenuItem BuildAccessLogItem()
        {
            bool on = _config?.Current?.HttpBridge?.AccessLog ?? true;

            var item = new ToolStripMenuItem("Access log");
            item.DropDownItems.Add(new ToolStripMenuItem("Record every request", null,
                (s, e) => _config?.SetAccessLog(!on))
            {
                Checked = on,
                ToolTipText = "Who connected, to which model, when. Safe to leave on: " +
                              "it never contains a query or its results."
            });

            string path = _accessLogPath?.Invoke();
            item.DropDownItems.Add(new ToolStripMenuItem("Open access log", null,
                (s, e) => AccessLogAction.Open(path))
            {
                Enabled = !string.IsNullOrEmpty(path),
                ToolTipText = path
            });

            return item;
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
            item.DropDownItems.Add(BuildAccessLogItem());
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
                    ToolTipText = BridgeAuthModeLabel.Describe(mode, status.Https)
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

            if (!status.IsUnauthenticated) return;

            // Disabled and unlabelled, this read as a greyed-out COMMAND rather than a
            // warning - it sat between Authentication and Restart looking like an action
            // someone had switched off, and said neither what had caused it nor what to
            // do about it. It is a consequence of the setting directly above it, so it
            // says which setting, and it offers the way out rather than describing one.
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(new ToolStripMenuItem(
                "⚠  Authentication is off — anyone who can reach port " + status.Port +
                " can read your models")
            {
                Enabled = false,
                ToolTipText =
                    "No authentication is set, so callers are not asked who they are. " +
                    "Served models can be read by anyone who can reach this port, and " +
                    "changed unless Read-only is set for that model.\n\n" +
                    "Set Authentication to Password sign-in to require a Windows account."
            });

            item.DropDownItems.Add(new ToolStripMenuItem("Require a password instead", null,
                (s, e) => _config?.SetEndpointAuthMode(BridgeAuthMode.Basic))
            {
                ToolTipText = "Callers sign in with a Windows account that exists on this machine."
            });
        }

        internal static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch { /* clipboard can transiently fail; ignore */ }
        }
    }
}
