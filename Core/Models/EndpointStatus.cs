namespace PBIRelay.Models
{
    /// <summary>
    /// What the XMLA endpoint is doing right now, as one immutable snapshot (#125).
    ///
    /// Both the tray and the dashboard project this same object, and the wording of
    /// <see cref="Summary"/> lives here rather than at each surface, so the two cannot
    /// drift into describing the same state differently — the lesson from the
    /// on-detection labels.
    /// </summary>
    public sealed class EndpointStatus
    {
        /// <summary>Whether the user has the endpoint switched on, regardless of success.</summary>
        public bool Enabled { get; }

        /// <summary>Whether a listener is actually accepting requests.</summary>
        public bool Running { get; }

        public int Port { get; }
        public BridgeAuthMode AuthMode { get; }

        /// <summary>The prefix actually bound, which may be the localhost fallback.</summary>
        public string BoundPrefix { get; }

        /// <summary>Why the endpoint is not running, when it was meant to be.</summary>
        public string Error { get; }

        /// <summary>Serving HTTPS (#132). Decides the scheme in every URL handed out.</summary>
        public bool Https { get; }

        /// <summary>What the certificate is and when it runs out, for the status line.</summary>
        public string CertificateSubject { get; }
        public DateTime? CertificateExpiry { get; }

        public EndpointStatus(
            bool enabled, bool running, int port, BridgeAuthMode authMode,
            string boundPrefix = null, string error = null, bool https = false,
            string certificateSubject = null, DateTime? certificateExpiry = null)
        {
            Enabled = enabled;
            Running = running;
            Port = port;
            AuthMode = authMode;
            BoundPrefix = boundPrefix;
            Error = error;
            Https = https;
            CertificateSubject = certificateSubject;
            CertificateExpiry = certificateExpiry;
        }

        /// <summary>
        /// Running with no authentication: anyone who can reach the port queries as the
        /// model owner, and can issue any XMLA command the owner could. Surfaces are
        /// expected to say so rather than leave it to the config file.
        /// </summary>
        public bool IsUnauthenticated => Running && AuthMode == BridgeAuthMode.Anonymous;

        /// <summary>
        /// Reachable from other machines, which running now simply means: Kestrel binds
        /// every address without a URL reservation, so the localhost fallback that used
        /// to make this false is gone (#132). Only the firewall can still stop a caller.
        /// </summary>
        public bool IsLanReachable => Running;

        /// <summary>One line describing the state, shared by every surface.</summary>
        public string Summary
        {
            get
            {
                if (!Enabled) return "Off";
                if (!Running) return string.IsNullOrEmpty(Error) ? "Not running" : $"Failed to start: {Error}";

                // The label, not the enum name: the tray menu shows the same mode, and
                // two names for one setting reads as two settings.
                string transport = Https ? "HTTPS, " : string.Empty;
                return $"Running on port {Port} ({transport}{BridgeAuthModeLabel.For(AuthMode)})";
            }
        }

        public static EndpointStatus Off(int port, BridgeAuthMode authMode) =>
            new EndpointStatus(enabled: false, running: false, port: port, authMode: authMode);
    }
}
