// NOTE: This file is reserved for future v0.2 XMLA protocol implementation
// Currently not in use - v0.1 uses TcpProxyService for simple TCP forwarding
// DO NOT DELETE - will be needed for database name abstraction feature

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PBIPortWrapper.Services
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
                Log($"Forwarding to port {targetPort}");
                Log($"Target database: {targetDatabase}");
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
                    target.NoDelay = true;
                    client.NoDelay = true;

                    await target.ConnectAsync("localhost", _targetPort);
                    Log($"Established connection to target port {_targetPort}");

                    using (target)
                    {
                        var clientStream = client.GetStream();
                        var targetStream = target.GetStream();

                        // Intercept client->target (rewrite database references)
                        var clientToTarget = InterceptXmlaAsync(clientStream, targetStream, true, cancellationToken);

                        // Forward target->client (no rewriting needed)
                        var targetToClient = InterceptXmlaAsync(targetStream, clientStream, false, cancellationToken);

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

        private async Task InterceptXmlaAsync(NetworkStream source, NetworkStream destination,
            bool rewriteDatabase, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;

                    byte[] dataToSend = buffer;
                    int lengthToSend = bytesRead;

                    if (rewriteDatabase)
                    {
                        // Try to detect and rewrite XMLA messages
                        // XMLA protocol sends messages with format: [4-byte length][payload]
                        // But we'll use a simpler approach: detect XML and rewrite it

                        try
                        {
                            // Check if this chunk contains XML
                            string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                            if (content.Contains("<DatabaseID>") ||
                                content.Contains("<CatalogName>") ||
                                content.Contains("<Catalog>") ||
                                content.Contains("Initial Catalog="))
                            {
                                string rewritten = RewriteDatabaseReferences(content);

                                if (rewritten != content)
                                {
                                    dataToSend = Encoding.UTF8.GetBytes(rewritten);
                                    lengthToSend = dataToSend.Length;
                                    Log($"Rewrote database reference in message");
                                }
                            }
                        }
                        catch
                        {
                            // If rewriting fails, send original data
                        }
                    }

                    await destination.WriteAsync(dataToSend, 0, lengthToSend, cancellationToken);
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
            string original = xmlContent;

            // Pattern 1: <DatabaseID>any-guid-or-name</DatabaseID>
            xmlContent = Regex.Replace(xmlContent,
                @"<DatabaseID>[^<]+</DatabaseID>",
                $"<DatabaseID>{_targetDatabase}</DatabaseID>",
                RegexOptions.IgnoreCase);

            // Pattern 2: <CatalogName>any-name</CatalogName>
            xmlContent = Regex.Replace(xmlContent,
                @"<CatalogName>[^<]+</CatalogName>",
                $"<CatalogName>{_targetDatabase}</CatalogName>",
                RegexOptions.IgnoreCase);

            // Pattern 3: <Catalog>name</Catalog>
            xmlContent = Regex.Replace(xmlContent,
                @"<Catalog>[^<]+</Catalog>",
                $"<Catalog>{_targetDatabase}</Catalog>",
                RegexOptions.IgnoreCase);

            // Pattern 4: Initial Catalog= in connection strings
            xmlContent = Regex.Replace(xmlContent,
                @"Initial\s+Catalog\s*=\s*[^;""<>\s]+",
                $"Initial Catalog={_targetDatabase}",
                RegexOptions.IgnoreCase);

            if (original != xmlContent)
            {
                Log($"Rewrote database name to: {_targetDatabase}");
            }

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