using System;

namespace PBIPortWrapper.Models
{
    public class PortMappingRule
    {
        public string ModelNamePattern { get; set; }
        public int FixedPort { get; set; }
        public bool AutoConnect { get; set; }
        public bool AllowNetworkAccess { get; set; }

        public PortMappingRule()
        {
        }

        public PortMappingRule(string modelNamePattern, int fixedPort, bool autoConnect, bool allowNetworkAccess)
        {
            ModelNamePattern = modelNamePattern;
            FixedPort = fixedPort;
            AutoConnect = autoConnect;
            AllowNetworkAccess = allowNetworkAccess;
        }
    }
}
