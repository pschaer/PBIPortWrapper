namespace PBIPortWrapper.Models
{
    /// <summary>
    /// Remote-leg authentication for the XMLA-over-HTTP bridge (#77).
    /// Each mode maps onto a built-in <see cref="System.Net.AuthenticationSchemes"/>
    /// value, so adding a mode is a switch case rather than an auth implementation.
    /// </summary>
    public enum BridgeAuthMode
    {
        /// <summary>
        /// Negotiate/NTLM. Only works when the host can authenticate the caller's
        /// Windows identity, i.e. on a domain. On a workgroup host a remote caller
        /// has no identity to present, the handshake fails, and the client is left
        /// waiting — which is the E1 barrier all over again, one layer up.
        /// </summary>
        Windows = 0,

        /// <summary>
        /// No authentication: anyone who can reach the port queries as the owner.
        /// Opt-in only.
        /// </summary>
        Anonymous = 1,

        /// <summary>
        /// HTTP Basic, checked against a Windows account on this machine by
        /// <see cref="PBIPortWrapper.Services.WindowsCredentialValidator"/> — the
        /// listener decodes the header but does not verify the password, so the check
        /// is ours. Works on a workgroup: give the remote user a local account here.
        /// Credentials are base64 over the wire, so this wants TLS outside a trusted
        /// LAN.
        /// </summary>
        Basic = 2
    }

    /// <summary>
    /// Settings for the XMLA-over-HTTP bridge (#77). Persisted inside
    /// <see cref="ProxyConfiguration"/>; absent in pre-v0.7.2 config files, which
    /// deserialize to these defaults (bridge off).
    /// </summary>
    public class HttpBridgeConfig
    {
        /// <summary>
        /// Off by default: the bridge exposes served models to the network, so it
        /// is never switched on implicitly.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 55555 — the port this tool has published since v0.1. It was briefly 55556
        /// while the endpoint coexisted with the TCP forwarder that owned 55555;
        /// forwarding retired in #126, so the original number is free again and is
        /// what users' notes, firewall rules and muscle memory already say.
        /// </summary>
        public int Port { get; set; } = 55555;

        /// <summary>
        /// Basic by default: it is the mode that works on a workgroup host, which is
        /// the common case for this tool. Windows/Negotiate only suits domain hosts.
        /// </summary>
        public BridgeAuthMode AuthMode { get; set; } = BridgeAuthMode.Basic;

        /// <summary>
        /// The host name to publish in the URLs handed to users, when the detected
        /// address is not the one they should use — a DNS name, or the right NIC on a
        /// multi-homed machine. Empty means detect it.
        ///
        /// This does not affect what the listener binds (it always binds all addresses
        /// or falls back to localhost); it only shapes the URLs shown. Changing it
        /// therefore must not restart the endpoint and interrupt live clients.
        /// </summary>
        public string Hostname { get; set; } = string.Empty;

        /// <summary>
        /// Write full SOAP request/response payloads to log.txt. Debugging only:
        /// payloads contain query results, and log.txt rotates at 5 MB.
        /// </summary>
        public bool LogPayloads { get; set; } = false;
    }
}
