using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    public class TcpProxyService : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private int _targetPort;
        private int _listenPort;
        private bool _isRunning;
        private int _activeConnections;
        private readonly ILogger _logger;
        private readonly string _modelName;

        public bool IsRunning => _isRunning;
        public int ListenPort => _listenPort;
        public int TargetPort => _targetPort;
        public int ActiveConnections => _activeConnections;

        public event EventHandler<int> OnConnectionCountChanged;

        public TcpProxyService(ILogger logger = null, string modelName = "Unknown")
        {
            _logger = logger;
            _modelName = modelName;
        }

        public async Task StartAsync(int listenPort, int targetPort, bool allowNetworkAccess)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Proxy is already running");
            }

            _listenPort = listenPort;
            _targetPort = targetPort;
            _activeConnections = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var ipAddress = allowNetworkAccess ? IPAddress.Any : IPAddress.Loopback;

                _listener = new TcpListener(ipAddress, listenPort);
                _listener.Start();
                _isRunning = true;

                string networkAccessMode = allowNetworkAccess ? "Network (0.0.0.0)" : "Localhost only (127.0.0.1)";
                _logger?.LogInfo("TcpProxy", $"Proxy started | Model: {_modelName} | Listen Port: {listenPort} | Target: localhost:{targetPort} | Access: {networkAccessMode}");
                
                _ = Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _logger?.LogError("TcpProxy", $"Failed to start proxy on port {listenPort} for model {_modelName}", ex);
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                _listener?.Stop();
                _isRunning = false;
                _logger?.LogInfo("TcpProxy", $"Proxy stopped | Model: {_modelName} | Port: {_listenPort}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("TcpProxy", $"Error stopping proxy on port {_listenPort}", ex);
            }
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _activeConnections);
                    OnConnectionCountChanged?.Invoke(this, _activeConnections);
                    
                    string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger?.LogConnectionInfo(remoteIp, _listenPort, _targetPort, _modelName);
                    
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped - normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger?.LogError("TcpProxy", $"Error accepting client on port {_listenPort}", ex);
                    }
                }
            }
        }

        private readonly List<ConnectionInfo> _activeConnectionDetails = new List<ConnectionInfo>();
        private readonly object _connectionLock = new object();

        public IReadOnlyList<ConnectionInfo> ActiveConnectionDetails
        {
            get
            {
                lock (_connectionLock)
                {
                    return _activeConnectionDetails.ToList();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            TcpClient target = null;
            string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            var connectionInfo = new ConnectionInfo
            {
                RemoteEndpoint = remoteIp,
                ConnectedAt = DateTime.Now,
                LocalPort = _listenPort,
                EstimatedTool = EstimateTool(client)
            };

            lock (_connectionLock)
            {
                _activeConnectionDetails.Add(connectionInfo);
            }

            try
            {
                using (client)
                {
                    target = new TcpClient
                    {
                        NoDelay = true
                    };

                    client.NoDelay = true;

                    await target.ConnectAsync("localhost", _targetPort);

                    using (target)
                    {
                        var clientStream = client.GetStream();
                        var targetStream = target.GetStream();

                        var clientToTarget = CopyStreamAsync(clientStream, targetStream, cancellationToken);
                        var targetToClient = CopyStreamAsync(targetStream, clientStream, cancellationToken);

                        await Task.WhenAny(clientToTarget, targetToClient);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogError("TcpProxy", $"Connection error from {remoteIp} on port {_listenPort}", ex);
                }
            }
            finally
            {
                lock (_connectionLock)
                {
                    _activeConnectionDetails.Remove(connectionInfo);
                }
                Interlocked.Decrement(ref _activeConnections);
                OnConnectionCountChanged?.Invoke(this, _activeConnections);
                _logger?.LogConnectionClosed(remoteIp, _listenPort, _activeConnections);
                target?.Dispose();
            }
        }

        private string EstimateTool(TcpClient client)
        {
            // Simple heuristic based on remote port or other characteristics could go here.
            // For now, we return "Unknown" as we don't inspect packets yet.
            // Improved heuristics can be added later (e.g. checking initial bytes).
            return "Unknown";
        }

        private async Task CopyStreamAsync(NetworkStream source, NetworkStream destination, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;

                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
            }
            catch (Exception)
            {
                // Connection closed - normal operation
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _listener?.Stop();
        }
    }
}
