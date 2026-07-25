using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Projects the detected models and their Off/Forward/Serve state into the
    /// tray menu, with per-model actions (#85a). This is the tray-first primary
    /// surface; it reuses the same serve/forward/stop plumbing the grid uses, so
    /// there is no behavior change to detection - only a new way to drive it.
    /// Auto-host-on-detection, toasts and the grace period are the next increment (#85b).
    /// </summary>
    public class TrayMenuManager
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ProxyManager _proxyManager;
        private readonly ServeSessionService _sessions;
        private readonly ProxyPresenter _proxyPresenter;
        private readonly ServeActionHandler _serveHandler;
        private readonly ConfigService _configService;
        private readonly Action _showDashboard;
        private readonly Action _exit;

        public TrayMenuManager(
            NotifyIcon notifyIcon,
            ContextMenuStrip menu,
            ProxyManager proxyManager,
            ServeSessionService sessions,
            ProxyPresenter proxyPresenter,
            ServeActionHandler serveHandler,
            ConfigService configService,
            Action showDashboard,
            Action exit)
        {
            _notifyIcon = notifyIcon;
            _menu = menu;
            _proxyManager = proxyManager;
            _sessions = sessions;
            _proxyPresenter = proxyPresenter;
            _serveHandler = serveHandler;
            _configService = configService;
            _showDashboard = showDashboard;
            _exit = exit;
        }

        /// <summary>Rebuilds the tray menu from the latest snapshot. UI thread only.</summary>
        public void Rebuild(IReadOnlyList<PowerBIInstance> instances)
        {
            _menu.Items.Clear();

            if (instances == null || instances.Count == 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("No Power BI models detected") { Enabled = false });
            }
            else
            {
                foreach (var instance in instances)
                    _menu.Items.Add(BuildModelItem(instance));
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Open dashboard…", null, (s, e) => _showDashboard?.Invoke()));
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => _exit?.Invoke()));
        }

        private ToolStripMenuItem BuildModelItem(PowerBIInstance instance)
        {
            var profile = _configService?.FindRule(instance.FileName);
            bool hasPort = profile != null && profile.FixedPort > 0;

            bool serving = _sessions?.FindSession(instance.WorkspaceId) != null;
            bool forwarding = hasPort && _proxyManager.IsRunning(profile.FixedPort);
            var state = HostStateMachine.CurrentState(serving, forwarding);

            string port = hasPort ? $"  :{profile.FixedPort}" : "";
            var item = new ToolStripMenuItem($"{instance.FileName}  —  {StateLabel(state)}{port}");

            if (!hasPort)
            {
                item.DropDownItems.Add(new ToolStripMenuItem(
                    "Set a fixed port in the dashboard first") { Enabled = false });
                return item;
            }

            foreach (var action in HostStateMachine.AvailableActions(state))
                item.DropDownItems.Add(BuildActionItem(instance, profile, action));

            item.DropDownItems.Add(BuildPolicyMenu(instance, profile));
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(BuildNetworkItem(instance, profile));
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(new ToolStripMenuItem("Copy connection string", null,
                (s, e) => CopyConnectionString(profile)));

            return item;
        }

        private ToolStripMenuItem BuildNetworkItem(PowerBIInstance instance, PortMappingRule profile)
        {
            return new ToolStripMenuItem("Allow network access", null,
                (s, e) => _configService?.SetNetwork(instance.FileName, !profile.AllowNetworkAccess))
            {
                Checked = profile.AllowNetworkAccess,
                ToolTipText = "Expose on the LAN (same Windows user only). Allow the port through the firewall. " +
                              "Takes effect the next time the proxy starts."
            };
        }

        private ToolStripMenuItem BuildPolicyMenu(PowerBIInstance instance, PortMappingRule profile)
        {
            var menu = new ToolStripMenuItem("On detection");
            foreach (var policy in OnDetectionPolicyLabel.Order)
            {
                var captured = policy;
                menu.DropDownItems.Add(new ToolStripMenuItem(OnDetectionPolicyLabel.For(policy), null,
                    (s, e) => _configService?.SetOnDetection(instance.FileName, captured))
                {
                    Checked = profile.OnDetection == policy
                });
            }
            return menu;
        }

        private ToolStripMenuItem BuildActionItem(PowerBIInstance instance, PortMappingRule profile, HostAction action)
        {
            string label = HostActionLabel.For(action);
            switch (action)
            {
                case HostAction.Serve:
                    return new ToolStripMenuItem(label, null,
                        async (s, e) => await _serveHandler.HandleServeAsync(instance));
                case HostAction.StopServing:
                    return new ToolStripMenuItem(label, null,
                        async (s, e) => await _serveHandler.HandleStopServingAsync(instance.WorkspaceId));
                case HostAction.Forward:
                    return new ToolStripMenuItem(label, null,
                        async (s, e) => await _proxyPresenter.StartProxyAsync(
                            instance, profile.FixedPort, profile.AllowNetworkAccess));
                case HostAction.Stop:
                    return new ToolStripMenuItem(label, null,
                        (s, e) => _proxyPresenter.StopProxy(profile.FixedPort, instance.WorkspaceId));
                default:
                    return new ToolStripMenuItem(label) { Enabled = false };
            }
        }

        private static void CopyConnectionString(PortMappingRule profile)
        {
            try { Clipboard.SetText(ConnectionEndpoint.For(profile)); }
            catch { /* clipboard can transiently fail; ignore */ }
        }

        private static string StateLabel(HostState state)
        {
            switch (state)
            {
                case HostState.Serve: return "Serving";
                case HostState.Forward: return "Forwarding";
                default: return "Off";
            }
        }
    }
}
