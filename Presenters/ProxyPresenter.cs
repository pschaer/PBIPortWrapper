using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    public class ProxyPresenter
    {
        private readonly ProxyManager _proxyManager;
        private readonly AutoConnectService _autoConnectService;
        private readonly Action<string> _logCallback;

        public ProxyPresenter(ProxyManager proxyManager, Action<string> logCallback)
        {
            _proxyManager = proxyManager;
            _logCallback = logCallback;
            _autoConnectService = new AutoConnectService(proxyManager, logCallback);
        }

        /// <summary>
        /// Auto-connect is config-driven (rule.AutoConnect), not grid-driven.
        /// Fire-and-forget: failures are logged inside the service.
        /// </summary>
        public void ProcessAutoConnect(IReadOnlyList<PowerBIInstance> instances, ProxyConfiguration config)
        {
            _ = _autoConnectService.ApplyAsync(instances, config);
        }

        public async Task StartProxyAsync(PowerBIInstance instance, int fixedPort, bool allowNetwork)
        {
            try
            {
                // An explicit Start ends any stop-suppression for this instance (#63).
                _autoConnectService.ClearSuppression(instance.WorkspaceId);

                if (_proxyManager.IsRunning(fixedPort)) return;

                if (fixedPort < 1024 || fixedPort > 65535)
                {
                    _logCallback($"Invalid port {fixedPort} for {instance.FileName}");
                    return;
                }

                await _proxyManager.StartProxyAsync(fixedPort, instance.Port, allowNetwork, instance.FileName);
            }
            catch (Exception ex)
            {
                _logCallback($"Failed to start {instance.FileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// User-initiated stop: suppresses auto-connect for the instance so the
        /// stop sticks while Auto stays checked (#63). Config-driven stops go
        /// through ProxyManager directly and do not suppress.
        /// </summary>
        public void StopProxy(int fixedPort, string workspaceId = null)
        {
            _autoConnectService.SuppressUntilExplicitStart(workspaceId);
            _proxyManager.StopProxy(fixedPort);
        }

        public void StopAll()
        {
            _proxyManager.StopAll();
        }
    }
}
