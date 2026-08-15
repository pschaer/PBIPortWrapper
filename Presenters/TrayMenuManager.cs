using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PBIRelay.Models;
using PBIRelay.Services;

namespace PBIRelay.Presenters
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
        private readonly ServeSessionService _sessions;
        private readonly ServeActionHandler _serveHandler;
        private readonly ConfigService _configService;
        private readonly XmlaEndpointCoordinator _endpoint;
        private readonly EndpointMenuBuilder _endpointMenu;
        private readonly Action _showDashboard;
        private readonly Action _exit;

        public TrayMenuManager(
            NotifyIcon notifyIcon,
            ContextMenuStrip menu,
            ServeSessionService sessions,
            ServeActionHandler serveHandler,
            ConfigService configService,
            XmlaEndpointCoordinator endpoint,
            Action showDashboard,
            Action exit,
            Func<string> accessLogPath = null)
        {
            _notifyIcon = notifyIcon;
            _menu = menu;
            _sessions = sessions;
            _serveHandler = serveHandler;
            _configService = configService;
            _endpoint = endpoint;
            _endpointMenu = new EndpointMenuBuilder(endpoint, configService, accessLogPath);
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
            _menu.Items.Add(_endpointMenu.Build());

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Open dashboard…", null, (s, e) => _showDashboard?.Invoke()));
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => _exit?.Invoke()));
        }

        private ToolStripMenuItem BuildModelItem(PowerBIInstance instance)
        {
            var profile = _configService?.FindRule(instance.FileName);
            bool hasAlias = !string.IsNullOrWhiteSpace(profile?.StableAlias);

            bool serving = _sessions?.FindSession(instance.WorkspaceId) != null;
            var state = HostStateMachine.CurrentState(serving);

            // The alias is the address now, so it is what the line shows — a port here
            // would name something no client uses (#126).
            string alias = hasAlias ? $"  ({profile.StableAlias})" : "";
            var item = new ToolStripMenuItem($"{instance.FileName}  —  {StateLabel(state)}{alias}");

            if (!hasAlias)
            {
                // Serving renames the database to the alias, so without one there is
                // nothing to serve it as. This is the only precondition left.
                item.DropDownItems.Add(new ToolStripMenuItem(
                    "Set a stable name in the dashboard first") { Enabled = false });
                item.DropDownItems.Add(BuildPolicyMenu(instance, profile));
                return item;
            }

            foreach (var action in HostStateMachine.AvailableActions(state))
                item.DropDownItems.Add(BuildActionItem(instance, profile, action));

            item.DropDownItems.Add(BuildPolicyMenu(instance, profile));
            item.DropDownItems.Add(BuildReadOnlyItem(instance, profile));

            // Connection details describe a live address, so they are offered only
            // while the model is actually served and the endpoint is up. At any other
            // time they would hand out something that does not resolve.
            if (serving && _endpoint?.Status?.Running == true)
            {
                item.DropDownItems.Add(new ToolStripSeparator());
                item.DropDownItems.Add(new ToolStripMenuItem("Copy endpoint URL", null,
                    (s, e) => EndpointMenuBuilder.CopyToClipboard(EndpointUrl(profile)))
                {
                    ToolTipText = "The address another machine uses for this model in Excel."
                });
                item.DropDownItems.Add(new ToolStripMenuItem("Copy connection string", null,
                    (s, e) => EndpointMenuBuilder.CopyToClipboard(
                        ConnectionStringBuilder.ForEndpoint(EndpointUrl(profile), profile.StableAlias))));
                item.DropDownItems.Add(new ToolStripMenuItem("Save .odc…", null,
                    (s, e) => SaveOdc(instance, profile)));
            }

            return item;
        }

        /// <summary>This model's address on the endpoint, as a client must write it.</summary>
        private string EndpointUrl(ModelRule profile)
        {
            HttpBridgeConfig config = _configService?.Current?.HttpBridge;
            EndpointStatus status = _endpoint?.Status;
            if (config == null || status == null) return string.Empty;

            return EndpointUrlBuilder.For(
                ConnectionEndpoint.EndpointHost(config, status), status.Port, profile.StableAlias,
                status.Https);
        }

        private void SaveOdc(PowerBIInstance instance, ModelRule profile) =>
            OdcSaveAction.Save(instance.FileName, EndpointUrl(profile), profile.StableAlias);

        /// <summary>
        /// The same value the grid's Read-only column edits (#129), through the same
        /// granular setter — the two surfaces project one setting, so they cannot drift
        /// apart the way the policy dropdown once did (#107).
        /// </summary>
        private ToolStripMenuItem BuildReadOnlyItem(PowerBIInstance instance, ModelRule profile)
        {
            bool on = profile?.ReadOnly ?? true;
            return new ToolStripMenuItem("Read-only", null,
                (s, e) => _configService?.SetReadOnly(instance.FileName, !on))
            {
                Checked = on,
                ToolTipText = "Refuse XMLA commands that would change this model. " +
                              "Clear it to allow write-back from a tool like Tabular Editor."
            };
        }

        private ToolStripMenuItem BuildPolicyMenu(PowerBIInstance instance, ModelRule profile)
        {
            var menu = new ToolStripMenuItem("On detection");
            foreach (var policy in OnDetectionPolicyLabel.Order)
            {
                var captured = policy;
                menu.DropDownItems.Add(new ToolStripMenuItem(OnDetectionPolicyLabel.For(policy), null,
                    (s, e) => _configService?.SetOnDetection(instance.FileName, captured))
                {
                    Checked = profile?.OnDetection == policy
                });
            }
            return menu;
        }

        private ToolStripMenuItem BuildActionItem(PowerBIInstance instance, ModelRule profile, HostAction action)
        {
            string label = HostActionLabel.For(action);
            switch (action)
            {
                case HostAction.Serve:
                    return new ToolStripMenuItem(label, null,
                        async (s, e) => await _serveHandler.HandleServeAsync(instance));
                case HostAction.Stop:
                    return new ToolStripMenuItem(label, null,
                        async (s, e) => await _serveHandler.HandleStopServingAsync(instance.WorkspaceId));
                default:
                    return new ToolStripMenuItem(label) { Enabled = false };
            }
        }

        private static string StateLabel(HostState state)
        {
            switch (state)
            {
                case HostState.Serve: return "Serving";
                default: return "Off";
            }
        }
    }
}
