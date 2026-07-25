using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// Builds the connection string external tools use for a hosted model,
    /// choosing localhost or the LAN address per the profile (#85). Shared by the
    /// tray menu and the auto-host toasts so the "copy" and "ready" strings match.
    /// </summary>
    public static class ConnectionEndpoint
    {
        public static string For(PortMappingRule rule)
        {
            if (rule == null || rule.FixedPort <= 0) return string.Empty;
            string host = rule.AllowNetworkAccess ? ResolveLocalAddress() : "localhost";
            return ConnectionStringBuilder.DataSource(host, rule.FixedPort);
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
