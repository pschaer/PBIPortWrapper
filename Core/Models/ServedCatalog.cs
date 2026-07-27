namespace PBIPortWrapper.Models
{
    /// <summary>
    /// One model currently reachable through the XMLA endpoint: the stable alias a
    /// client asks for, and the live Analysis Services port answering for it.
    ///
    /// This is the endpoint's whole view of the world. It deliberately knows nothing
    /// about serve sessions, detection or configuration — the endpoint routes by name
    /// and nothing else.
    /// </summary>
    public sealed class ServedCatalog
    {
        public string Alias { get; }
        public int Port { get; }

        /// <summary>
        /// Whether this model refuses Execute commands that would change it (#129).
        /// Per-model rather than one endpoint-wide switch, because each model answers
        /// on its own path and the relay therefore always knows which one a request is
        /// for — unlike reachability, which is one listener and could only be global.
        /// </summary>
        public bool ReadOnly { get; }

        public ServedCatalog(string alias, int port, bool readOnly = true)
        {
            Alias = alias;
            Port = port;
            ReadOnly = readOnly;
        }
    }
}
