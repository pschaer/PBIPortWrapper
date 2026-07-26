using System;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Builds the URL a client uses to reach one served model through the XMLA
    /// endpoint (#125): host, port and the model's own path (#136).
    ///
    /// Pure string building — choosing the host is the caller's job, because that
    /// depends on whether the listener is LAN-reachable. The alias is percent-encoded
    /// exactly as <see cref="XmlaRelay.ModelFromPath"/> decodes it, so a name with a
    /// space survives the round trip.
    /// </summary>
    public static class EndpointUrlBuilder
    {
        public static string For(string host, int port, string alias)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(alias))
                return string.Empty;

            return $"http://{host}:{port}/{Uri.EscapeDataString(alias.Trim())}";
        }

        /// <summary>
        /// The one-time command that makes the endpoint reachable from other machines.
        /// Without a URL ACL the listener falls back to localhost, which looks like the
        /// endpoint working while no remote client can reach it — so this is offered
        /// wherever that fallback is reported.
        /// </summary>
        public static string UrlAclCommand(int port) =>
            $"netsh http add urlacl url=http://+:{port}/ user=Everyone";
    }
}
