namespace PBIPortWrapper.Models
{
    /// <summary>
    /// The proxy state of a model in the tray-first workflow (v0.7, #84):
    /// a single Off → Forward → Serve axis that replaces the separate
    /// Start/Stop and Serve/Stop-Serving actions. <c>Serve</c> is <c>Forward</c>
    /// plus a database rename to the stable alias.
    /// </summary>
    public enum HostState
    {
        /// <summary>Not proxied.</summary>
        Off = 0,

        /// <summary>Stable port forwarded; database keeps its session GUID; Desktop editable.</summary>
        Forward = 1,

        /// <summary>Stable port forwarded and database renamed to the alias; Desktop blocked while served.</summary>
        Serve = 2
    }

    /// <summary>
    /// A user (or automated) request to change a model's <see cref="HostState"/>.
    /// The mapping to a resulting state lives in
    /// <see cref="Services.HostStateMachine"/>.
    /// </summary>
    public enum HostAction
    {
        /// <summary>Go to Forward (stable port only).</summary>
        Forward = 0,

        /// <summary>Go to Serve (rename + forward).</summary>
        Serve = 1,

        /// <summary>Stop serving but keep forwarding (drop the rename so Desktop is editable): Serve → Forward.</summary>
        StopServing = 2,

        /// <summary>Stop everything: → Off.</summary>
        Stop = 3
    }
}
