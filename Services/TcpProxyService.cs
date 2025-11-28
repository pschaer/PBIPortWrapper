using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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

        public bool IsRunning => _isRunning;
        public int ListenPort => _listenPort;
        public int TargetPort => _targetPort;
        public int ActiveConnections => _activeConnections;

        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;
        public event EventHandler<int> OnConnectionCountChanged;

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

                Log($"TCP Proxy started on port {listenPort}");
                Log($"Forwarding to localhost:{targetPort}");
                Log($"Network access: {(allowNetworkAccess ? "Enabled (accessible from network)" : "Disabled (localhost only)")}");

                _ = Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                _isRunning = false;
                LogError($"Failed to start proxy: {ex.Message}");
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
                Log("Proxy stopped");
            }
            catch (Exception ex)
            {
                LogError($"Error stopping proxy: {ex.Message}");
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
                    Log($"Client connected from {client.Client.RemoteEndPoint} (Active: {_activeConnections})");

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
                        LogError($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            TcpClient target = null;

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
                    LogError($"Connection error: {ex.Message}");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeConnections);
                OnConnectionCountChanged?.Invoke(this, _activeConnections);
                Log($"Client disconnected (Active: {_activeConnections})");
                target?.Dispose();
            }
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

        private void Log(string message)
        {
            OnLog?.Invoke(this, message);
        }

        private void LogError(string message)
        {
            OnError?.Invoke(this, $"ERROR: {message}");
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _listener?.Stop();
        }
    }
}