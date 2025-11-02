using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PowerBIPortWrapper.Services
{
    public class TcpProxyService
    {
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private int _targetPort;
        private int _listenPort;
        private bool _isRunning;

        public bool IsRunning => _isRunning;
        public int ListenPort => _listenPort;
        public int TargetPort => _targetPort;

        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;

        public async Task StartAsync(int listenPort, int targetPort, bool allowRemote = false)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Proxy is already running");
            }

            _listenPort = listenPort;
            _targetPort = targetPort;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Bind to all interfaces if remote access is allowed, otherwise localhost only
                var ipAddress = allowRemote ? IPAddress.Any : IPAddress.Loopback;
                _listener = new TcpListener(ipAddress, listenPort);
                _listener.Start();
                _isRunning = true;

                Log($"Proxy started on port {listenPort}, forwarding to port {targetPort}");
                Log($"Network access: {(allowRemote ? "Enabled" : "Localhost only")}");

                // Accept connections in background
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
                    Log($"Client connected from {client.Client.RemoteEndPoint}");

                    // Handle each client in a separate task
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
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
                    // Connect to target
                    target = new TcpClient();
                    await target.ConnectAsync("localhost", _targetPort);

                    Log($"Established connection to target port {_targetPort}");

                    using (target)
                    {
                        var clientStream = client.GetStream();
                        var targetStream = target.GetStream();

                        // Bidirectional copy
                        var clientToTarget = CopyStreamAsync(clientStream, targetStream, cancellationToken);
                        var targetToClient = CopyStreamAsync(targetStream, clientStream, cancellationToken);

                        // Wait for either direction to complete
                        await Task.WhenAny(clientToTarget, targetToClient);

                        Log("Client disconnected");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogError($"Error handling client: {ex.Message}");
                }
            }
            finally
            {
                target?.Close();
            }
        }

        private async Task CopyStreamAsync(NetworkStream source, NetworkStream destination, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
            }
            catch (Exception)
            {
                // Connection closed or error - this is normal
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void LogError(string message)
        {
            OnError?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] ERROR: {message}");
        }
    }
}
