using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// HTTP front end for the XMLA endpoint (#77): accepts SOAP XMLA over HTTP and
    /// hands each envelope, with the path it arrived on, to <see cref="XmlaRelay"/>.
    ///
    /// The listener binds the port's root so every model can have its own path —
    /// <c>http://host:55555/Sales</c> — which is what keeps one address to one engine
    /// (#136). The path is passed on verbatim; resolving it to a model is the relay's
    /// job, where it is testable.
    ///
    /// <see cref="BridgeAuthMode"/> maps onto built-in
    /// <see cref="AuthenticationSchemes"/>, so the handshake is the listener's. The
    /// one thing the listener does not do is check a Basic password — it decodes the
    /// header, reports the claimed name and admits the request — so that check happens
    /// here, against Windows, storing nothing. See
    /// <see cref="WindowsCredentialValidator"/>.
    /// </summary>
    public class HttpBridgeService : IXmlaEndpoint
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private HttpBridgeConfig _config;

        private readonly XmlaRelay _relay;
        private readonly ILogger _logger;

        private int _consecutiveAcceptFailures;
        private const int MaxConsecutiveAcceptFailures = 20;

        public bool IsRunning { get; private set; }

        /// <summary>The prefix actually bound, which may be the localhost fallback.</summary>
        public string BoundPrefix { get; private set; }

        /// <summary>True when bound to localhost only, i.e. not reachable from the LAN.</summary>
        public bool IsLocalOnly { get; private set; }

        public HttpBridgeService(XmlaRelay relay, ILogger logger = null)
        {
            _relay = relay ?? throw new ArgumentNullException(nameof(relay));
            _logger = logger;
        }

        /// <summary>
        /// Binds the listener and starts accepting. Synchronous and throws on
        /// failure: a bridge that could not bind must not be reported as running.
        /// </summary>
        public void Start(HttpBridgeConfig config)
        {
            if (IsRunning) throw new InvalidOperationException("HTTP Bridge is already running.");

            _config = config ?? throw new ArgumentNullException(nameof(config));
            _cts = new CancellationTokenSource();

            // The port's root, so that every served model gets a path under it.
            string wildcard = $"http://+:{_config.Port}/";
            string loopback = $"http://localhost:{_config.Port}/";

            try
            {
                _listener = Bind(wildcard);
                IsLocalOnly = false;
            }
            catch (HttpListenerException)
            {
                // Binding a wildcard prefix needs either elevation or a URL ACL. Fall
                // back to loopback so the bridge still works on this machine, but say
                // so loudly — the LAN case is the whole point of the feature.
                _listener = Bind(loopback);
                IsLocalOnly = true;
                _logger?.LogWarning("HttpBridge",
                    $"Bound to {loopback} (this machine only). For LAN access run once as admin: " +
                    $"netsh http add urlacl url={wildcard} user=Everyone");
            }

            BoundPrefix = IsLocalOnly ? loopback : wildcard;
            IsRunning = true;

            _logger?.LogInfo("HttpBridge", $"XMLA bridge listening on {BoundPrefix} (auth: {_config.AuthMode})");
            WarnIfNegotiateCannotWork();

            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        private HttpListener Bind(string prefix)
        {
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(prefix);
                listener.AuthenticationSchemes = ToSchemes(_config.AuthMode);
                listener.Start();
                return listener;
            }
            catch
            {
                listener.Close();
                throw;
            }
        }

        public static AuthenticationSchemes ToSchemes(BridgeAuthMode mode)
        {
            switch (mode)
            {
                case BridgeAuthMode.Anonymous:
                    return AuthenticationSchemes.Anonymous;
                case BridgeAuthMode.Basic:
                    return AuthenticationSchemes.Basic;
                default:
                    return AuthenticationSchemes.IntegratedWindowsAuthentication;
            }
        }

        /// <summary>
        /// Negotiate needs the host to be able to authenticate the caller's Windows
        /// identity. On a workgroup host a remote caller has none, the NTLM handshake
        /// fails inside the listener, and the client is left waiting with no reply —
        /// which looks exactly like a hang. Say so at startup rather than at 2am.
        /// </summary>
        private void WarnIfNegotiateCannotWork()
        {
            if (_config.AuthMode != BridgeAuthMode.Windows) return;
            if (IsDomainJoined() != false) return;

            _logger?.LogWarning("HttpBridge",
                "AuthMode=Windows on a workgroup host: remote callers have no Windows identity this " +
                "machine accepts, so the Negotiate handshake will fail and clients will appear to hang. " +
                "Use AuthMode=Basic (2) with a local account, or Anonymous (1) on a trusted LAN.");
        }

        /// <summary>True/false when determinable, null when it cannot be established.</summary>
        private static bool? IsDomainJoined()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "SELECT PartOfDomain FROM Win32_ComputerSystem"))
                using (var results = searcher.Get())
                {
                    foreach (System.Management.ManagementBaseObject mo in results)
                    {
                        object value = mo["PartOfDomain"];
                        if (value != null) return (bool)value;
                    }
                }
            }
            catch { /* not worth failing startup over a diagnostic */ }
            return null;
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
                _logger?.LogInfo("HttpBridge", "XMLA bridge stopped.");
            }
            catch (Exception ex)
            {
                _logger?.LogError("HttpBridge", $"Error stopping XMLA bridge: {ex.Message}", ex);
            }
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsRunning)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // A failed authentication handshake throws out here, which means
                    // the client never got a reply and is sitting there waiting. We
                    // cannot answer a context we never received, so the useful thing
                    // is to say precisely why, once, and keep serving other clients.
                    _consecutiveAcceptFailures++;
                    _logger?.LogError("HttpBridge",
                        $"Could not accept a request ({ex.Message}). The client received no reply and " +
                        $"will appear to hang. Auth mode is {_config.AuthMode}.", ex);

                    if (_consecutiveAcceptFailures >= MaxConsecutiveAcceptFailures)
                    {
                        _logger?.LogError("HttpBridge",
                            $"Giving up after {_consecutiveAcceptFailures} consecutive accept failures; " +
                            "stopping the bridge rather than spinning. Check the auth mode.");
                        break;
                    }
                    continue;
                }

                _consecutiveAcceptFailures = 0;
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
        }

        public async Task HandleRequestAsync(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            try
            {
                HttpListenerRequest request = context.Request;

                if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(response, (int)HttpStatusCode.MethodNotAllowed, "text/plain",
                        "The XMLA endpoint accepts POST only.").ConfigureAwait(false);
                    return;
                }

                if (!IsAuthenticated(context))
                {
                    // Challenge again rather than just refusing, so a client that
                    // mistyped a password gets another prompt instead of an error.
                    response.AddHeader("WWW-Authenticate", "Basic realm=\"PBI Port Wrapper\"");
                    await WriteAsync(response, (int)HttpStatusCode.Unauthorized, "text/plain",
                        "Invalid user name or password.").ConfigureAwait(false);
                    return;
                }

                string soapRequest;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                {
                    soapRequest = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                string caller = context.User?.Identity?.Name ?? "anonymous";
                string soapAction = request.Headers["SOAPAction"];
                string path = request.Url?.AbsolutePath;

                // One line per request, and Excel sends ~50 per session — too much for
                // a log the dashboard shows. Who connected and when is worth keeping,
                // but that is access logging (#128), not a per-request trace.
                _logger?.LogDebug("HttpBridge",
                    $"Request from {request.RemoteEndPoint} as {caller} | Path: {path ?? "(none)"} " +
                    $"| SOAPAction: {soapAction ?? "(none)"}");

                if (_config.LogPayloads)
                {
                    _logger?.LogDebug("HttpBridge", $"[REQUEST]\n{soapRequest}");
                }

                string soapResponse = _relay.Relay(soapRequest, soapAction, path);

                if (_config.LogPayloads)
                {
                    _logger?.LogDebug("HttpBridge", $"[RESPONSE]\n{soapResponse}");
                }

                await WriteAsync(response, (int)HttpStatusCode.OK, "text/xml; charset=utf-8", soapResponse)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError("HttpBridge", $"Error handling request: {ex.Message}", ex);
                try { response.StatusCode = (int)HttpStatusCode.InternalServerError; } catch { }
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        /// <summary>
        /// Whether the caller has proved who they are.
        ///
        /// Only Basic needs anything of us: Negotiate/NTLM is verified by Windows
        /// during the listener's own handshake, and Anonymous asserts nothing by
        /// design. For Basic the listener has already decoded a name and password and
        /// checked neither, so an unvalidated request here is an unauthenticated one.
        /// </summary>
        private bool IsAuthenticated(HttpListenerContext context)
        {
            if (_config.AuthMode != BridgeAuthMode.Basic) return true;

            var identity = context.User?.Identity as HttpListenerBasicIdentity;
            if (identity == null)
            {
                _logger?.LogWarning("HttpBridge",
                    $"Rejected {context.Request.RemoteEndPoint}: no credentials supplied.");
                return false;
            }

            if (WindowsCredentialValidator.IsValid(identity.Name, identity.Password)) return true;

            // The name is logged, never the password. A rejected sign-in is the one
            // thing an owner needs to see when someone cannot get in - or when someone
            // is trying to.
            _logger?.LogWarning("HttpBridge",
                $"Rejected {context.Request.RemoteEndPoint}: '{identity.Name}' is not a valid " +
                "Windows account on this machine, or the password was wrong.");
            return false;
        }

        private static async Task WriteAsync(HttpListenerResponse response, int statusCode, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await response.OutputStream.FlushAsync().ConfigureAwait(false);
        }
    }
}
