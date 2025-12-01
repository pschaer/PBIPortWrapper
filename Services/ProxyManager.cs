using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PBIPortWrapper.Services
{
    public class ProxyManager
    {
        private readonly ConcurrentDictionary<int, TcpProxyService> _proxies;
        private readonly ILogger _logger;
        private readonly Dictionary<int, string> _modelNames; // Map ports to model names

        public event EventHandler<ProxyEventArgs> OnProxyStarted;
        public event EventHandler<ProxyEventArgs> OnProxyStopped;
        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;
        public event EventHandler<(int FixedPort, int Count)> OnProxyConnectionCountChanged;

        public ProxyManager(ILogger logger = null)
        {
            _proxies = new ConcurrentDictionary<int, TcpProxyService>();
            _logger = logger;
            _modelNames = new Dictionary<int, string>();
        }

                public async Task StartProxyAsync(int fixedPort, int targetPort, bool allowNetworkAccess, string modelName = "Unknown")
        {
            if (_proxies.ContainsKey(fixedPort))
            {
                if (_proxies[fixedPort].IsRunning) return;
                _proxies.TryRemove(fixedPort, out _);
            }

            var proxy = new TcpProxyService(_logger, modelName);
            // Prefix log messages with the port number for clarity
            proxy.OnLog += (s, msg) => OnLog?.Invoke(this, $"[Port {fixedPort}] {msg}");
            proxy.OnError += (s, msg) => OnError?.Invoke(this, $"[Port {fixedPort}] {msg}");
            
            // Bubble up connection count changes
            proxy.OnConnectionCountChanged += (s, count) => OnProxyConnectionCountChanged?.Invoke(this, (fixedPort, count));

            _proxies[fixedPort] = proxy;
            _modelNames[fixedPort] = modelName;
            
            _logger?.LogInfo("ProxyManager", $"Starting proxy for {modelName} | Port: {fixedPort} -> {targetPort}");

            await proxy.StartAsync(fixedPort, targetPort, allowNetworkAccess);
            
            OnProxyStarted?.Invoke(this, new ProxyEventArgs(fixedPort, targetPort));
        }

                public void StopProxy(int fixedPort)
        {
            if (_proxies.TryRemove(fixedPort, out var proxy))
            {
                try
                {
                    string modelName = _modelNames.TryGetValue(fixedPort, out var name) ? name : "Unknown";
                    _logger?.LogInfo("ProxyManager", $"Stopping proxy for {modelName} | Port: {fixedPort}");
                    
                    proxy.Stop();
                    proxy.Dispose();
                    _modelNames.Remove(fixedPort);
                    OnProxyStopped?.Invoke(this, new ProxyEventArgs(fixedPort, 0));
                }
                catch (Exception ex)
                {
                    _logger?.LogError("ProxyManager", $"Error stopping proxy on port {fixedPort}", ex);
                    OnError?.Invoke(this, $"Error stopping proxy on port {fixedPort}: {ex.Message}");
                }
            }
        }

                public void StopAll()
        {
            _logger?.LogInfo("ProxyManager", $"Stopping all proxies ({_proxies.Count} active)");
            foreach (var port in _proxies.Keys.ToList())
            {
                StopProxy(port);
            }
        }

        public bool IsRunning(int fixedPort)
        {
            return _proxies.ContainsKey(fixedPort) && _proxies[fixedPort].IsRunning;
        }

        public bool HasRunningProxies()
        {
            return _proxies.Any(p => p.Value.IsRunning);
        }

        public int GetActiveConnections(int fixedPort)
        {
            if (_proxies.TryGetValue(fixedPort, out var proxy))
            {
                return proxy.ActiveConnections;
            }
            return 0;
        }

        public IEnumerable<int> GetRunningPorts()
        {
            return _proxies.Keys.ToList();
        }
    }

    public class ProxyEventArgs : EventArgs
    {
        public int FixedPort { get; }
        public int TargetPort { get; }

        public ProxyEventArgs(int fixedPort, int targetPort)
        {
            FixedPort = fixedPort;
            TargetPort = targetPort;
        }
    }
}
