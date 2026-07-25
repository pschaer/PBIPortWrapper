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
        /// Legacy (pre-v0.7): "start a serve session automatically on detection".
        /// Never actually consumed by detection; superseded by <see cref="OnDetection"/>,
        /// into which the config migration folds it (#84). Kept so old config files
        /// deserialize and the migration can read the old value.
        /// </summary>
        public bool AutoServe { get; set; }

        /// <summary>
        /// What to do when this model's Desktop instance is detected (v0.7, #84).
        /// The authoritative per-model policy that supersedes AutoConnect/AutoServe.
        /// Defaults to <see cref="OnDetectionPolicy.DoNothing"/>; the config migration
        /// derives it from the legacy booleans for pre-v0.7 config files.
        /// </summary>
        public OnDetectionPolicy OnDetection { get; set; } = OnDetectionPolicy.DoNothing;

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
