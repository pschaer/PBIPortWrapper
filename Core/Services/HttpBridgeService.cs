using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using PBIRelay.Models;

namespace PBIRelay.Services
{
    /// <summary>
    /// HTTP front end for the XMLA endpoint (#77): accepts SOAP XMLA over HTTP and
    /// hands each envelope, with the path it arrived on, to <see cref="XmlaRelay"/>.
    ///
    /// It binds the port's root so every model can have its own path —
    /// <c>http://host:55555/Sales</c> — which is what keeps one address to one engine
    /// (#136). The path is passed on verbatim; resolving it to a model is the relay's
    /// job, where it is testable.
    ///
    /// **Kestrel, not HttpListener (#132).** HttpListener runs on http.sys, where
    /// binding anything but localhost needs an administrative <c>netsh http add
    /// urlacl</c>, and serving TLS needs an administrative <c>netsh http add
    /// sslcert</c>. Kestrel binds the socket itself, so both work as an ordinary user:
    /// the endpoint gained HTTPS and lost an administrative setup step in the same
    /// change. Only the firewall rule remains.
    ///
    /// The cost is that Basic is ours to check rather than the listener's. That is a
    /// header decode plus <see cref="WindowsCredentialValidator"/> — no credential is
    /// stored, and Windows remains the only thing that says yes. It also removes a
    /// blind spot: http.sys used to answer the 401 challenge itself, so a client that
    /// never answered it was invisible to every log we wrote (#149). Now every request
    /// reaches this code, so every request can be recorded.
    /// </summary>
    public class HttpBridgeService : IXmlaEndpoint
    {
        private WebApplication _app;
        private HttpBridgeConfig _config;
        private readonly XmlaClientRegistry _clients = new XmlaClientRegistry();
        private readonly IAccessLog _accessLog;

        private readonly XmlaRelay _relay;
        private readonly ILogger _logger;

        /// <summary>
        /// How long shutdown waits for the endpoint. Bounded on purpose: exiting the
        /// application must not depend on a socket letting go.
        /// </summary>
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A bare challenge is a protocol step, not an access event, so it is not
        /// written to the access log - see the comment where it is set.
        /// </summary>
        private const string Challenged = "challenged";

        /// <summary>
        /// Clients retry a rejected sign-in, so one wrong password produced three
        /// identical warnings. One per minute per account and address, with a count.
        /// </summary>
        private readonly RepeatedEventLimiter _signInFailures =
            new RepeatedEventLimiter(TimeSpan.FromMinutes(1));

        private X509Certificate2 _certificate;
        private string _certificateSource;
        private DateTime _certificateLoadedUtc;
        private readonly object _certificateLock = new object();

        /// <summary>
        /// How stale the cached certificate may get. Short enough that a renewal is live
        /// within minutes, long enough that a busy endpoint is not re-reading a file or
        /// the certificate store on every connection.
        /// </summary>
        private static readonly TimeSpan CertificateRecheck = TimeSpan.FromMinutes(5);

        /// <summary>The certificate being served, or null when running as plain HTTP.</summary>
        public X509Certificate2 Certificate
        {
            get { lock (_certificateLock) { return _certificate; } }
        }

        public bool IsRunning { get; private set; }

        /// <summary>The address actually bound.</summary>
        public string BoundPrefix { get; private set; }

        public HttpBridgeService(XmlaRelay relay, ILogger logger = null, IAccessLog accessLog = null)
        {
            _relay = relay ?? throw new ArgumentNullException(nameof(relay));
            _logger = logger;
            _accessLog = accessLog;
        }

        /// <summary>
        /// Binds and starts accepting. Synchronous and throws on failure: a bridge that
        /// could not bind must not be reported as running.
        /// </summary>
        public void Start(HttpBridgeConfig config)
        {
            if (IsRunning) throw new InvalidOperationException("HTTP Bridge is already running.");

            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Resolved before binding, so a missing certificate fails to start with a
            // reason rather than starting and refusing every connection.
            _certificate = null;
            if (_config.UseHttps)
            {
                CertificateResolution resolved = CertificateResolver.Resolve(
                    _config.CertificatePath, _config.CertificateThumbprint, _config.CertificateKeyPath);

                if (!resolved.Ok) throw new InvalidOperationException(resolved.Problem);

                _certificate = resolved.Certificate;
                _certificateSource = resolved.Source;
                _certificateLoadedUtc = DateTime.UtcNow;
            }

            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrel(options => options.ListenAnyIP(_config.Port, listen =>
            {
                if (!_config.UseHttps) return;

                // A SELECTOR rather than a fixed certificate, because this app runs for
                // months from a login while a Let's Encrypt certificate is replaced every
                // sixty days or so. One captured at startup would quietly go stale and
                // present as "clients suddenly cannot connect", so the current one is
                // re-read periodically and picked up on the next connection, with no
                // restart and nothing for anyone to remember.
                listen.UseHttps(httpsOptions =>
                    httpsOptions.ServerCertificateSelector = (_, _) => CurrentCertificate());
            }));
            builder.Logging.ClearProviders();
            builder.Services.AddRoutingCore();

            WebApplication app = builder.Build();
            app.UseRouting();

            // One catch-all: which model a request is for is the path's business, and
            // the relay's to resolve. Routing it here would put that decision in two
            // places, and the one in the relay is the tested one.
            app.Run(HandleRequestAsync);

            try
            {
                // Task.Run, not a direct wait: Start and Stop are called from the UI
                // thread, and blocking it on work whose continuation needs that same
                // thread deadlocks. Task.Run leaves the WinForms synchronization context
                // behind, so the continuation has somewhere else to run.
                Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();
            }
            catch
            {
                try { Task.Run(() => app.DisposeAsync().AsTask()).Wait(ShutdownTimeout); } catch { }
                throw;
            }

            _app = app;
            _clients.Reset();            // a run is the unit a diagnosis is scoped to (#149)
            _signInFailures.Reset();
            IsRunning = true;
            BoundPrefix = $"{(_config.UseHttps ? "https" : "http")}://+:{_config.Port}/";

            BridgeAuthMode effective = EffectiveAuthMode(_config.AuthMode);
            _logger?.LogInfo("HttpBridge", $"XMLA bridge listening on {BoundPrefix} (auth: {effective})");

            if (_certificate != null)
            {
                _logger?.LogInfo("HttpBridge",
                    $"HTTPS using {_certificate.Subject} from {_certificateSource}, " +
                    $"valid until {_certificate.NotAfter:yyyy-MM-dd}.");

                // Said at startup, where somebody might act on it, rather than discovered
                // by a client failing to connect one morning.
                int daysLeft = (int)(_certificate.NotAfter - DateTime.Now).TotalDays;
                if (daysLeft < 0)
                {
                    _logger?.LogWarning("HttpBridge",
                        $"That certificate EXPIRED on {_certificate.NotAfter:yyyy-MM-dd}; clients will refuse it.");
                }
                else if (daysLeft <= 14)
                {
                    _logger?.LogWarning("HttpBridge",
                        $"That certificate expires in {daysLeft} day(s). A renewal is picked up automatically.");
                }
            }

            if (effective != _config.AuthMode)
            {
                _logger?.LogWarning("HttpBridge",
                    "This configuration still asks for Windows sign-in, which is no longer offered - " +
                    "it never worked on a machine that is not domain-joined. Using password sign-in " +
                    "instead. Pick an authentication mode in the endpoint settings to clear this.");
            }
        }

        /// <summary>
        /// The certificate to serve a connection with, re-reading the source once the
        /// cached one has been held long enough.
        ///
        /// Never throws and never returns null once started: a failed re-read keeps the
        /// certificate already in hand, because a momentarily unreadable file is exactly
        /// what a renewal looks like while it is being written, and that must not take
        /// the endpoint down.
        /// </summary>
        private X509Certificate2 CurrentCertificate()
        {
            lock (_certificateLock)
            {
                if (DateTime.UtcNow - _certificateLoadedUtc < CertificateRecheck) return _certificate;
                _certificateLoadedUtc = DateTime.UtcNow;

                try
                {
                    CertificateResolution resolved = CertificateResolver.Resolve(
                        _config.CertificatePath, _config.CertificateThumbprint, _config.CertificateKeyPath);

                    if (!resolved.Ok)
                    {
                        _logger?.LogWarning("HttpBridge",
                            $"Could not re-read the certificate ({resolved.Problem}); " +
                            "continuing with the one already loaded.");
                        return _certificate;
                    }

                    if (resolved.Certificate.Thumbprint == _certificate?.Thumbprint)
                    {
                        resolved.Certificate.Dispose();
                        return _certificate;
                    }

                    _logger?.LogInfo("HttpBridge",
                        $"Certificate renewed: now {resolved.Certificate.Subject}, " +
                        $"valid until {resolved.Certificate.NotAfter:yyyy-MM-dd}.");

                    X509Certificate2 previous = _certificate;
                    _certificate = resolved.Certificate;
                    _certificateSource = resolved.Source;
                    previous?.Dispose();
                    return _certificate;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("HttpBridge",
                        $"Could not re-read the certificate ({ex.Message}); " +
                        "continuing with the one already loaded.");
                    return _certificate;
                }
            }
        }

        /// <summary>
        /// Stops accepting and releases the port.
        ///
        /// This runs on the UI thread during application shutdown, so it must never
        /// block on work that needs the UI thread to finish - doing exactly that left
        /// the app with its window closed, a dead tray icon and a process that had to be
        /// killed. Hence Task.Run, which drops the WinForms context, and a bounded wait,
        /// so that an endpoint refusing to stop delays exit rather than preventing it.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            WebApplication app = _app;
            _app = null;

            lock (_certificateLock)
            {
                _certificate?.Dispose();
                _certificate = null;
            }

            if (app == null) return;

            try
            {
                bool stopped = Task.Run(async () =>
                {
                    await app.StopAsync().ConfigureAwait(false);
                    await app.DisposeAsync().ConfigureAwait(false);
                }).Wait(ShutdownTimeout);

                _logger?.LogInfo("HttpBridge", stopped
                    ? "XMLA bridge stopped."
                    : $"XMLA bridge did not stop within {ShutdownTimeout.TotalSeconds:N0}s; " +
                      "closing anyway and letting it finish in the background.");
            }
            catch (Exception ex)
            {
                _logger?.LogError("HttpBridge", $"Error stopping XMLA bridge: {ex.Message}", ex);
            }
        }

        public async Task HandleRequestAsync(HttpContext context)
        {
            HttpRequest request = context.Request;

            // Measured around everything the caller waits for, because a duration that
            // excluded the engine would describe this process rather than the request.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string userAgent = request.Headers.UserAgent.ToString();

            var access = new AccessLogEntry
            {
                RemoteAddress = context.Connection.RemoteIpAddress?.ToString(),
                Client = XmlaClientRegistry.Describe(userAgent),
                Caller = "anonymous",
                Model = XmlaRelay.ModelFromPath(request.Path.Value),
                Outcome = "error"
            };

            try
            {
                // Every request reaches this line, including one that is about to be
                // challenged - which is what http.sys never allowed (#149).
                if (_clients.IsNew(userAgent, "arrived from " + access.RemoteAddress))
                {
                    _logger?.LogInfo("HttpBridge",
                        $"XMLA request arriving from {access.RemoteAddress} [{access.Client}].");
                }

                if (!HttpMethods.IsPost(request.Method))
                {
                    access.Verb = request.Method;
                    access.Outcome = "not-allowed";
                    _logger?.LogInfo("HttpBridge",
                        $"{request.Method} from {access.RemoteAddress} [{access.Client}] refused: " +
                        "the endpoint accepts POST only.");
                    await WriteAsync(context, StatusCodes.Status405MethodNotAllowed, "text/plain",
                        "The XMLA endpoint accepts POST only.").ConfigureAwait(false);
                    return;
                }

                if (!Authenticate(context, access, out string caller))
                {
                    // Challenge rather than just refuse: a client that has not sent
                    // credentials yet is asking to be told how, and one that mistyped a
                    // password gets another prompt instead of an error.
                    context.Response.Headers.WWWAuthenticate = "Basic realm=\"PBIRelay\"";
                    await WriteAsync(context, StatusCodes.Status401Unauthorized, "text/plain",
                        "Invalid user name or password.").ConfigureAwait(false);
                    return;
                }

                access.Caller = caller;

                string soapRequest;
                using (var reader = new StreamReader(request.Body, Encoding.UTF8))
                {
                    soapRequest = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                string soapAction = request.Headers["SOAPAction"];
                string path = request.Path.Value;

                var parsed = XmlaRequestSummary.ParseOrNull(soapRequest);
                access.Verb = XmlaRelay.ResolveVerb(parsed, soapAction).ToString();
                access.Detail = XmlaRequestSummary.Describe(parsed);

                if (_clients.IsNew(userAgent, "signed in as " + caller))
                {
                    _logger?.LogInfo("HttpBridge",
                        $"XMLA client [{access.Client}] from {access.RemoteAddress} signed in as {caller}.");
                }

                // One line per request, and Excel sends ~50 per session — too much for
                // a log the dashboard shows. Who connected and when is worth keeping,
                // but that is the access log, not a per-request trace.
                _logger?.LogDebug("HttpBridge",
                    $"Request from {access.RemoteAddress} as {caller} [{access.Client}] " +
                    $"| Path: {path ?? "(none)"} | SOAPAction: {soapAction ?? "(none)"}");

                if (_config.LogPayloads) _logger?.LogDebug("HttpBridge", $"[REQUEST]\n{soapRequest}");

                string soapResponse = _relay.Relay(soapRequest, soapAction, path);

                if (_config.LogPayloads) _logger?.LogDebug("HttpBridge", $"[RESPONSE]\n{soapResponse}");

                // A fault is still a completed request, and one worth telling apart:
                // "who connected" is less useful than "who connected and got nowhere".
                access.Outcome = XmlaRequestSummary.IsFault(soapResponse) ? "fault" : "ok";

                await WriteAsync(context, StatusCodes.Status200OK, "text/xml; charset=utf-8", soapResponse)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError("HttpBridge", $"Error handling request: {ex.Message}", ex);
                try { context.Response.StatusCode = StatusCodes.Status500InternalServerError; } catch { }
            }
            finally
            {
                if (_config != null && _config.AccessLog && access.Outcome != Challenged)
                {
                    access.DurationMs = clock.ElapsedMilliseconds;
                    _accessLog?.Write(access);
                }
            }
        }

        /// <summary>
        /// Whether the caller has proved who they are, and who that turned out to be.
        ///
        /// Only Basic checks anything: Anonymous asserts nothing by design. The header
        /// is decoded here and the name and password handed straight to Windows —
        /// nothing is stored, and this is not a credential store.
        /// </summary>
        private bool Authenticate(HttpContext context, AccessLogEntry access, out string caller)
        {
            caller = "anonymous";
            if (EffectiveAuthMode(_config.AuthMode) != BridgeAuthMode.Basic) return true;

            string header = context.Request.Headers.Authorization.ToString();
            if (!BasicCredentials.TryParse(header, out string user, out string password))
            {
                // Not a failure: sending nothing and being challenged is the FIRST HALF
                // of every Basic exchange, and Excel opens a new connection per request,
                // so this happens once per request even when everything is working. At
                // Warning it buried the log in reports of the protocol working (#132).
                //
                // The durable record is the access log, and "did anything arrive at all"
                // is answered once per client by the arrival line.
                // Deliberately NOT recorded in the access log. A client sends nothing,
                // is challenged, and retries WITH credentials - so writing this would
                // put a blank row in front of every genuine one and double a file whose
                // entire purpose is to be readable. The request that carried credentials
                // is the access event; this is the handshake that preceded it.
                //
                // A client that never authenticates at all is still visible: it is
                // announced once per run by the arrival line above.
                access.Outcome = Challenged;
                _logger?.LogDebug("HttpBridge",
                    $"Challenged {access.RemoteAddress} [{access.Client}]: no credentials yet.");
                return false;
            }

            if (!WindowsCredentialValidator.IsValid(user, password))
            {
                // This one IS worth a warning: somebody supplied credentials and they
                // were wrong. The name is logged, never the password. It is what an
                // owner needs to see when someone cannot get in - or is trying to.
                access.Outcome = "unauthorized";

                // Every attempt is in the access log; this decides what is worth saying.
                // Clients retry, so one wrong password typed once arrived here three
                // times and said the same sentence three times.
                if (_signInFailures.ShouldReport($"{user}@{access.RemoteAddress}", DateTime.UtcNow, out int alsoFailed))
                {
                    string more = alsoFailed > 0
                        ? $" ({alsoFailed} further attempts since the last warning)"
                        : string.Empty;

                    _logger?.LogWarning("HttpBridge",
                        $"Rejected {access.RemoteAddress} [{access.Client}]: '{user}' is not a valid " +
                        $"Windows account on this machine, or the password was wrong.{more}");
                }
                return false;
            }

            caller = user;
            return true;
        }

        /// <summary>
        /// Negotiate is no longer offered (#164): it needs the host to authenticate the
        /// caller's Windows identity, which a workgroup host cannot do, so it never
        /// worked here. The enum value stays so old config files still deserialize, and
        /// it resolves to Basic rather than to nothing — a stored setting that meant
        /// "authenticate callers" must never quietly come to mean "do not".
        /// </summary>
        public static BridgeAuthMode EffectiveAuthMode(BridgeAuthMode configured) =>
            configured == BridgeAuthMode.Windows ? BridgeAuthMode.Basic : configured;

        private static async Task WriteAsync(HttpContext context, int statusCode, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
    }
}
