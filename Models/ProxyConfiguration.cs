using System.Collections.Generic;

namespace PBIPortWrapper.Models
{
    public class ProxyConfiguration
    {
        public int FixedPort { get; set; } = 55555;
        public bool AllowNetworkAccess { get; set; } = false;
        public string LastSelectedInstance { get; set; }
        public List<PortMappingRule> PortMappings { get; set; } = new List<PortMappingRule>();
    }
}