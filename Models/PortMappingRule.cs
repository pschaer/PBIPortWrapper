using System;

namespace PBIPortWrapper.Models
{
    public class PortMappingRule
    {
        public string ModelNamePattern { get; set; }
        public string FilePath { get; set; } // New property for disambiguation
        public int FixedPort { get; set; }
        public bool AutoConnect { get; set; }
        public bool AllowNetworkAccess { get; set; }

        public PortMappingRule(string modelNamePattern, int fixedPort, bool autoConnect, bool allowNetworkAccess, string filePath = null)
        {
            ModelNamePattern = modelNamePattern;
            FixedPort = fixedPort;
            AutoConnect = autoConnect;
            AllowNetworkAccess = allowNetworkAccess;
            FilePath = filePath;
        }
    }
}
