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

        public event EventHandler<ProxyEventArgs> OnProxyStarted;
        public event EventHandler<ProxyEventArgs> OnProxyStopped;
        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;
        public ProxyManager()
        {
            _proxies = new ConcurrentDictionary<int, TcpProxyService>();
        }

        public async Task StartProxyAsync(int fixedPort, int targetPort, bool allowNetworkAccess)
        {
            if (_proxies.ContainsKey(fixedPort))
            {
                if (_proxies[fixedPort].IsRunning) return;
                _proxies.TryRemove(fixedPort, out _);
            }

            var proxy = new TcpProxyService();
            proxy.OnLog += (s, msg) => OnLog?.Invoke(this, msg);
            proxy.OnError += (s, msg) => OnError?.Invoke(this, msg);

            _proxies[fixedPort] = proxy;

            await proxy.StartAsync(fixedPort, targetPort, allowNetworkAccess);
            
            OnProxyStarted?.Invoke(this, new ProxyEventArgs(fixedPort, targetPort));
        }

        public void StopProxy(int fixedPort)
        {
            if (_proxies.TryRemove(fixedPort, out var proxy))
            {
                try
                {
                    proxy.Stop();
                    proxy.Dispose();
                    OnProxyStopped?.Invoke(this, new ProxyEventArgs(fixedPort, 0));
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Error stopping proxy on port {fixedPort}: {ex.Message}");
                }
            }
        }

        public void StopAll()
        {
            foreach (var port in _proxies.Keys.ToList())
            {
                StopProxy(port);
            }
        }

        public bool IsRunning(int fixedPort)
        {
            return _proxies.ContainsKey(fixedPort) && _proxies[fixedPort].IsRunning;
        }

        public int GetActiveConnections(int fixedPort)
        {
            if (_proxies.TryGetValue(fixedPort, out var proxy))
            {
                return proxy.ActiveConnections;
            }
            return 0;
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
