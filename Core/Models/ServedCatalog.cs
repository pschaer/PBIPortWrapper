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

        public ServedCatalog(string alias, int port)
        {
            Alias = alias;
            Port = port;
        }
    }
}
