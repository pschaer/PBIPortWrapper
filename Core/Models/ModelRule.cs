using System;
using Newtonsoft.Json;

namespace PBIPortWrapper.Models
{
    public class ModelRule
    {
        public string ModelNamePattern { get; set; }

        /// <summary>
        /// Legacy (pre-v1.0): "forward on detect". Forwarding is retired (#126) and
        /// nothing reads this any more; it survives only so a pre-v1 config file can
        /// still be migrated. <c>FixedPort</c> and <c>AllowNetworkAccess</c> went with
        /// it — a model is addressed by alias on the XMLA endpoint, whose port and
        /// reachability are one global setting. Both are simply absent now; old config
        /// files still load, because unknown JSON properties are ignored.
        /// </summary>
        public bool AutoConnect { get; set; }

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

        public ModelRule()
        {
        }

        public ModelRule(string modelNamePattern, string stableAlias = null)
        {
            ModelNamePattern = modelNamePattern;
            StableAlias = stableAlias;
        }
    }
}
