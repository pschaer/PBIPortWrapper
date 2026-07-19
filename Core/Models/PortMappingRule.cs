using System;
using Newtonsoft.Json;

namespace PBIPortWrapper.Models
{
    public class PortMappingRule
    {
        public string ModelNamePattern { get; set; }
        public int FixedPort { get; set; }
        public bool AutoConnect { get; set; }
        public bool AllowNetworkAccess { get; set; }

        /// <summary>
        /// Stable Initial Catalog the database is renamed to while serving.
        /// Serialized as "RenamedDatabaseName" so pre-v0.5 config files load unchanged.
        /// </summary>
        [JsonProperty("RenamedDatabaseName")]
        public string StableAlias { get; set; }

        /// <summary>
        /// Start a serve session automatically on detection (rename + forward).
        /// Distinct from AutoConnect, which only forwards the port.
        /// </summary>
        public bool AutoServe { get; set; }

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
