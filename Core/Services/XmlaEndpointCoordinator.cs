using System;
using PBIRelay.Models;

namespace PBIRelay.Services
{
    /// <summary>
    /// Owns the XMLA endpoint's lifetime, so its settings can be changed while the app
    /// runs instead of only at launch (#125).
    ///
    /// Configuration stays the single source of truth: surfaces write a setting through
    /// <see cref="ConfigService"/>, and this reacts to <c>ConfigurationChanged</c> by
    /// bringing the listener into line. That keeps the tray and the dashboard from
    /// each needing their own copy of the start/stop logic.
    ///
    /// It restarts the listener only when a setting the listener actually uses has
    /// changed. <c>ConfigurationChanged</c> fires for every serve, rule edit and policy
    /// change — bouncing the endpoint on each of those would drop live client
    /// connections for no reason.
    /// </summary>
    public class XmlaEndpointCoordinator : IDisposable
    {
        /// <summary>
        /// The settings a running listener is bound with. Hostname is deliberately not
        /// here: it only shapes the connection strings shown to users, so changing it
        /// must not interrupt anyone's session.
        /// </summary>
        private readonly struct ListenerSettings : IEquatable<ListenerSettings>
        {
            public readonly bool Enabled;
            public readonly int Port;
            public readonly BridgeAuthMode AuthMode;

            /// <summary>
            /// The certificate settings belong here, unlike Hostname: they decide what
            /// the listener BINDS, not merely what the URLs say. Left out, switching
            /// HTTPS on would change every published URL to https:// while the listener
            /// kept serving plain HTTP - handing out addresses that cannot connect,
            /// which is the exact failure the scheme work set out to prevent.
            ///
            /// Certificate RENEWAL does not come through here: the paths stay the same
            /// and the reload happens per connection, so a renewal never restarts the
            /// listener or drops a client.
            /// </summary>
            public readonly bool UseHttps;
            public readonly string Certificate;

            private ListenerSettings(
                bool enabled, int port, BridgeAuthMode authMode, bool useHttps, string certificate)
            {
                Enabled = enabled;
                Port = port;
                AuthMode = authMode;
                UseHttps = useHttps;
                Certificate = certificate;
            }

            public static ListenerSettings From(HttpBridgeConfig config) =>
                new ListenerSettings(config.Enabled, config.Port, config.AuthMode, config.UseHttps,
                    $"{config.CertificateThumbprint}|{config.CertificatePath}|{config.CertificateKeyPath}");

            public bool Equals(ListenerSettings other) =>
                Enabled == other.Enabled && Port == other.Port && AuthMode == other.AuthMode &&
                UseHttps == other.UseHttps &&
                string.Equals(Certificate, other.Certificate, StringComparison.OrdinalIgnoreCase);

            public override bool Equals(object obj) => obj is ListenerSettings other && Equals(other);

            public override int GetHashCode() =>
                (Enabled ? 1 : 0) ^ (Port << 1) ^ ((int)AuthMode << 20) ^ (UseHttps ? 1 << 30 : 0) ^
                (Certificate == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Certificate));
        }

        private readonly IXmlaEndpoint _endpoint;
        private readonly ConfigService _config;
        private readonly ILogger _logger;
        private readonly object _applyLock = new object();

        private ListenerSettings _applied;
        private bool _hasApplied;
        private bool _disposed;

        public XmlaEndpointCoordinator(IXmlaEndpoint endpoint, ConfigService config, ILogger logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;

            Status = EndpointStatus.Off(CurrentConfig()?.Port ?? 0, CurrentConfig()?.AuthMode ?? BridgeAuthMode.Basic);
            _config.ConfigurationChanged += OnConfigurationChanged;
        }

        /// <summary>The latest snapshot. Never null.</summary>
        public EndpointStatus Status { get; private set; }

        /// <summary>
        /// Raised after every apply. May arrive on a background thread — a serve
        /// completing on a worker can be what triggered the configuration change — so
        /// UI subscribers must marshal.
        /// </summary>
        public event EventHandler<EndpointStatus> StatusChanged;

        /// <summary>
        /// Brings the listener into line with configuration. Does nothing when no
        /// listener-relevant setting has changed.
        /// </summary>
        public void ApplyConfiguration() => Apply(force: false);

        /// <summary>
        /// Stops and starts unconditionally — the retry after a bind failure, and what
        /// a "Restart endpoint" action calls. A failed start leaves the settings
        /// recorded as applied, so this is the way back rather than an automatic retry
        /// on every unrelated configuration change.
        /// </summary>
        public void Restart() => Apply(force: true);

        private HttpBridgeConfig CurrentConfig() => _config.Current?.HttpBridge;

        private void OnConfigurationChanged(object sender, EventArgs e) => ApplyConfiguration();

        private void Apply(bool force)
        {
            EndpointStatus published;

            lock (_applyLock)
            {
                if (_disposed) return;

                HttpBridgeConfig config = CurrentConfig();
                if (config == null) return;

                // Before the change check: LogPayloads is not a listener setting, so
                // toggling it alone must still take effect.
                ApplyDiagnosticLevel(config);

                ListenerSettings wanted = ListenerSettings.From(config);
                if (!force && _hasApplied && wanted.Equals(_applied)) return;

                _applied = wanted;
                _hasApplied = true;

                if (_endpoint.IsRunning)
                {
                    _endpoint.Stop();
                }

                string error = null;
                if (wanted.Enabled)
                {
                    try
                    {
                        _endpoint.Start(config);
                    }
                    catch (Exception ex)
                    {
                        // A port already in use is the ordinary case here, and it must
                        // reach the user as status rather than only log.txt.
                        error = ex.Message;
                        _logger?.LogError("XmlaEndpoint",
                            $"Endpoint failed to start on port {config.Port}: {ex.Message}", ex);
                    }
                }

                published = Snapshot(config, error);
                Status = published;
            }

            StatusChanged?.Invoke(this, published);
        }

        /// <summary>
        /// Routing detail and SOAP payloads are logged at Debug, so they stay out of a
        /// dashboard that would otherwise take ~50 lines per Excel session. LogPayloads
        /// is the existing "tell me everything" switch, so it is what lowers the
        /// threshold — one diagnostic control rather than two.
        /// </summary>
        private void ApplyDiagnosticLevel(HttpBridgeConfig config)
        {
            if (!(_logger is ILogLevelSwitch levelSwitch)) return;

            LogLevel wanted = config.LogPayloads ? LogLevel.Debug : LogLevel.Info;
            if (levelSwitch.MinimumLevel == wanted) return;

            levelSwitch.MinimumLevel = wanted;
            _logger.LogInfo("XmlaEndpoint", $"Diagnostic logging {(config.LogPayloads ? "on" : "off")}.");
        }

        private EndpointStatus Snapshot(HttpBridgeConfig config, string error) =>
            new EndpointStatus(
                enabled: config.Enabled,
                running: _endpoint.IsRunning,
                port: config.Port,
                authMode: config.AuthMode,
                boundPrefix: _endpoint.IsRunning ? _endpoint.BoundPrefix : null,
                error: error,
                // What is actually being served while it runs, and the configured
                // intention while it is not. Reporting the intention of a RUNNING
                // listener would let a failed HTTPS start still publish https:// URLs.
                https: _endpoint.IsRunning ? BoundScheme() : config.UseHttps,
                certificateSubject: Certificate()?.Subject,
                certificateExpiry: Certificate()?.NotAfter);

        /// <summary>Whether the running listener actually bound HTTPS.</summary>
        private bool BoundScheme() =>
            _endpoint.BoundPrefix?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>The certificate in use, when the endpoint is one that has one.</summary>
        private System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate() =>
            (_endpoint as HttpBridgeService)?.Certificate;

        public void Dispose()
        {
            EndpointStatus published;

            lock (_applyLock)
            {
                if (_disposed) return;
                _disposed = true;

                _config.ConfigurationChanged -= OnConfigurationChanged;
                if (_endpoint.IsRunning) _endpoint.Stop();

                HttpBridgeConfig config = CurrentConfig();
                published = config == null
                    ? EndpointStatus.Off(0, BridgeAuthMode.Basic)
                    : EndpointStatus.Off(config.Port, config.AuthMode);
                Status = published;
            }

            StatusChanged?.Invoke(this, published);
        }
    }
}
