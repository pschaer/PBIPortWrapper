using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Starts proxies for detected instances whose config rule has AutoConnect set.
    /// Decisions are driven entirely by configuration and the instance snapshot —
    /// not by UI state.
    /// </summary>
    public class AutoConnectService
    {
        private readonly ProxyManager _proxyManager;
        private readonly Action<string> _logCallback;

        // Instances the user explicitly stopped (#63): auto-connect must not
        // resurrect them until the user clicks Start or the instance goes away.
        private readonly HashSet<string> _suppressedWorkspaces = new HashSet<string>();
        private readonly object _suppressionLock = new object();

        public AutoConnectService(ProxyManager proxyManager, Action<string> logCallback = null)
        {
            _proxyManager = proxyManager;
            _logCallback = logCallback;
        }

        /// <summary>Marks an instance as user-stopped: ApplyAsync skips it until
        /// <see cref="ClearSuppression"/> or the instance leaves the snapshot.</summary>
        public void SuppressUntilExplicitStart(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            lock (_suppressionLock) _suppressedWorkspaces.Add(workspaceId);
        }

        public void ClearSuppression(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            lock (_suppressionLock) _suppressedWorkspaces.Remove(workspaceId);
        }

        public bool IsSuppressed(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return false;
            lock (_suppressionLock) return _suppressedWorkspaces.Contains(workspaceId);
        }

        public async Task ApplyAsync(IReadOnlyList<PowerBIInstance> instances, ProxyConfiguration config)
        {
            if (instances == null) return;

            // A suppression is transient intent tied to a live instance; once the
            // instance is gone, the next appearance is a fresh session.
            lock (_suppressionLock)
            {
                _suppressedWorkspaces.RemoveWhere(id => !instances.Any(i => i.WorkspaceId == id));
            }

            if (config?.PortMappings == null) return;

            foreach (var instance in instances)
            {
                var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);
                if (rule == null || !rule.AutoConnect) continue;

                if (IsSuppressed(instance.WorkspaceId)) continue;

                if (rule.FixedPort < 1024 || rule.FixedPort > 65535)
                {
                    _logCallback?.Invoke($"Invalid port {rule.FixedPort} for {instance.FileName}");
                    continue;
                }

                if (_proxyManager.IsRunning(rule.FixedPort)) continue;

                try
                {
                    await _proxyManager.StartProxyAsync(rule.FixedPort, instance.Port, rule.AllowNetworkAccess, instance.FileName);
                }
                catch (Exception ex)
                {
                    _logCallback?.Invoke($"Failed to start {instance.FileName}: {ex.Message}");
                }
            }
        }
    }
}
