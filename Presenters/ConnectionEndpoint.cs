using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using PBIRelay.Models;
using PBIRelay.Services;

namespace PBIRelay.Presenters
{
    /// <summary>
    /// Builds the connection string external tools use for a hosted model,
    /// choosing localhost or the LAN address per the profile (#85). Shared by the
    /// tray menu and the auto-host toasts so the "copy" and "ready" strings match.
    /// </summary>
    public static class ConnectionEndpoint
    {

        
        /// <summary>
        /// The host to publish in XMLA endpoint URLs (#125): the configured host name
        /// when the user set one, otherwise the LAN address while the endpoint is
        /// actually reachable from other machines, otherwise localhost.
        ///
        /// The fallback matters: handing out a LAN address while the listener fell back
        /// to localhost produces a URL that simply never connects.
        /// </summary>
        public static string EndpointHost(HttpBridgeConfig config, EndpointStatus status)
        {
            if (!string.IsNullOrWhiteSpace(config?.Hostname)) return config.Hostname.Trim();
            return status != null && status.IsLanReachable ? ResolveLocalAddress() : "localhost";
        }

        /// <summary>Best-effort local IPv4 for LAN connection strings.</summary>
        private static string ResolveLocalAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint ep) return ep.Address.ToString();
                }
            }
            catch
            {
                try
                {
                    var ip = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ip != null) return ip.ToString();
                }
                catch { }
            }
            return "localhost";
        }
    }
}
