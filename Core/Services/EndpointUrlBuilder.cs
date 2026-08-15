using System;

namespace PBIRelay.Services
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
        public static string For(string host, int port, string alias, bool https = false)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(alias))
                return string.Empty;

            // The scheme has to come from what the endpoint is actually serving (#132).
            // Every connection string, .odc file and copied URL is built here, so a
            // scheme fixed at "http" would hand out addresses that do not connect the
            // moment HTTPS is switched on - and each one would look perfectly correct.
            string scheme = https ? "https" : "http";
            return $"{scheme}://{host}:{port}/{Uri.EscapeDataString(alias.Trim())}";
        }
    }
}
