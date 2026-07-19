using System;

namespace PBIPortWrapper.Models
{
    public class ConnectionInfo
    {
        public string RemoteEndpoint { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string EstimatedTool { get; set; }
        public int LocalPort { get; set; }

        public TimeSpan Duration => DateTime.Now - ConnectedAt;
    }
}
