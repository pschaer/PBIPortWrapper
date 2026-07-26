namespace PBIPortWrapper.Models
{
    /// <summary>
    /// Whether a model is being served (#126). Forwarding was the third state on this
    /// axis until v1.0 retired it: it gave a model a stable TCP port but no stable
    /// name, and only for the same Windows user — precisely what the XMLA endpoint
    /// replaced.
    /// </summary>
    public enum HostState
    {
        /// <summary>Detected, but not reachable through the endpoint.</summary>
        Off = 0,

        /// <summary>
        /// Database renamed to the stable alias and answering on the endpoint at its
        /// own path. Power BI Desktop cannot edit the model while it is served.
        /// </summary>
        Serve = 1
    }

    /// <summary>
    /// A user (or automated) request to change a model's <see cref="HostState"/>.
    /// The mapping to a resulting state lives in
    /// <see cref="Services.HostStateMachine"/>.
    /// </summary>
    public enum HostAction
    {
        /// <summary>Rename to the alias and publish it on the endpoint.</summary>
        Serve = 0,

        /// <summary>Stop serving and restore the database's original name.</summary>
        Stop = 1
    }
}
