using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AnalysisServices.AdomdClient;

namespace PowerBIPortWrapper.Services
{
    public class XmlaProxyService
    {
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private int _targetPort;
        private string _targetDatabase;
        private int _listenPort;
        private bool _isRunning;

        public bool IsRunning => _isRunning;
        public int ListenPort => _listenPort;
        public int TargetPort => _targetPort;
        public string TargetDatabase => _targetDatabase;

        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;

        public async Task StartAsync(int listenPort, int targetPort, string targetDatabase, bool allowRemote = false)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Proxy is already running");
            }

            _listenPort = listenPort;
            _targetPort = targetPort;
            _targetDatabase = targetDatabase;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var ipAddress = allowRemote ? IPAddress.Any : IPAddress.Loopback;
                _listener = new TcpListener(ipAddress, listenPort);
                _listener.Start();
                _isRunning = true;

                Log($"XMLA Proxy started on port {listenPort}");
                Log($"Forwarding to port {targetPort}, database: {targetDatabase}");
                Log($"Network access: {(allowRemote ? "Enabled" : "Localhost only")}");

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
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
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
                    target = new TcpClient();
                    await target.ConnectAsync("localhost", _targetPort);
                    Log($"Established connection to target port {_targetPort}");

                    using (target)
                    {
                        var clientStream = client.GetStream();
                        var targetStream = target.GetStream();

                        // Intercept and rewrite XMLA messages
                        var clientToTarget = InterceptAndForwardAsync(clientStream, targetStream, true, cancellationToken);
                        var targetToClient = InterceptAndForwardAsync(targetStream, clientStream, false, cancellationToken);

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

        private async Task InterceptAndForwardAsync(NetworkStream source, NetworkStream destination,
            bool rewriteDatabase, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            var messageBuffer = new MemoryStream();

            try
            {
                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;

                    if (rewriteDatabase)
                    {
                        // Accumulate data to detect complete XMLA messages
                        messageBuffer.Write(buffer, 0, bytesRead);

                        // Try to process if we have XML-like content
                        string content = Encoding.UTF8.GetString(messageBuffer.ToArray());

                        if (content.Contains("</Envelope>") || content.Contains("</SOAP-ENV:Envelope>"))
                        {
                            // We have a complete SOAP message, rewrite it
                            string rewritten = RewriteDatabaseReferences(content);
                            byte[] rewrittenBytes = Encoding.UTF8.GetBytes(rewritten);

                            await destination.WriteAsync(rewrittenBytes, 0, rewrittenBytes.Length, cancellationToken);
                            await destination.FlushAsync(cancellationToken);

                            messageBuffer.SetLength(0); // Clear buffer
                            continue;
                        }
                    }

                    // Forward as-is if not rewriting or incomplete message
                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log($"Stream error: {ex.Message}");
                }
            }
        }

        private string RewriteDatabaseReferences(string xmlContent)
        {
            // Replace any database GUID with our target database
            // This handles various XMLA formats

            // Pattern 1: <DatabaseID>guid</DatabaseID>
            xmlContent = Regex.Replace(xmlContent,
                @"<DatabaseID>[a-fA-F0-9\-]+</DatabaseID>",
                $"<DatabaseID>{_targetDatabase}</DatabaseID>",
                RegexOptions.IgnoreCase);

            // Pattern 2: CatalogName attribute
            xmlContent = Regex.Replace(xmlContent,
                @"CatalogName\s*=\s*['""][a-fA-F0-9\-]+['""]",
                $"CatalogName=\"{_targetDatabase}\"",
                RegexOptions.IgnoreCase);

            // Pattern 3: Catalog in connection string
            xmlContent = Regex.Replace(xmlContent,
                @"(Initial\s+Catalog|Database)\s*=\s*[a-fA-F0-9\-]+",
                $"Initial Catalog={_targetDatabase}",
                RegexOptions.IgnoreCase);

            return xmlContent;
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