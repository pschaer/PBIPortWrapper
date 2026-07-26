namespace PBIPortWrapper.Models
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

        /// <summary>Bound to localhost only: reachable from this machine, not the LAN.</summary>
        public bool IsLocalOnly { get; }

        /// <summary>Why the endpoint is not running, when it was meant to be.</summary>
        public string Error { get; }

        public EndpointStatus(
            bool enabled, bool running, int port, BridgeAuthMode authMode,
            string boundPrefix = null, bool isLocalOnly = false, string error = null)
        {
            Enabled = enabled;
            Running = running;
            Port = port;
            AuthMode = authMode;
            BoundPrefix = boundPrefix;
            IsLocalOnly = isLocalOnly;
            Error = error;
        }

        /// <summary>
        /// Running with no authentication: anyone who can reach the port queries as the
        /// model owner, and can issue any XMLA command the owner could. Surfaces are
        /// expected to say so rather than leave it to the config file.
        /// </summary>
        public bool IsUnauthenticated => Running && AuthMode == BridgeAuthMode.Anonymous;

        /// <summary>
        /// Reachable from other machines. False while the endpoint fell back to
        /// localhost, which is the failure users hit without a URL ACL.
        /// </summary>
        public bool IsLanReachable => Running && !IsLocalOnly;

        /// <summary>One line describing the state, shared by every surface.</summary>
        public string Summary
        {
            get
            {
                if (!Enabled) return "Off";
                if (!Running) return string.IsNullOrEmpty(Error) ? "Not running" : $"Failed to start: {Error}";

                // Only the exception is worth stating. The endpoint always binds every
                // interface, and there is no per-model network switch for it, so being
                // reachable from the network is simply what running means — saying
                // "LAN" every time carries no information and invites confusion with
                // forwarding's per-model "Allow network access". The localhost
                // fallback is the anomaly, so that is what gets named.
                string reach = IsLocalOnly ? "this machine only, " : string.Empty;

                // The label, not the enum name: the tray menu shows the same mode, and
                // two names for one setting reads as two settings.
                return $"Running on port {Port} ({reach}{BridgeAuthModeLabel.For(AuthMode)})";
            }
        }

        public static EndpointStatus Off(int port, BridgeAuthMode authMode) =>
            new EndpointStatus(enabled: false, running: false, port: port, authMode: authMode);
    }
}
