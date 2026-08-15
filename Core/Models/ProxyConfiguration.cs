using System.Collections.Generic;

namespace PBIRelay.Models
{
    // Top-level FixedPort/AllowNetworkAccess were dead v0.1 leftovers (#59), and the
    // per-rule ones went with forwarding (#126). Newtonsoft ignores unknown members,
    // so old config files containing them still load.
    public class ProxyConfiguration
    {
        /// <summary>
        /// Config schema version, for idempotent migrations (#84). Absent in
        /// pre-v0.7 files, so they deserialize as 0 and get upgraded on load by
        /// <see cref="Services.ConfigMigrator"/>.
        /// </summary>
        public int ConfigVersion { get; set; } = 0;

        public bool MinimizeToTray { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public string LastSelectedInstance { get; set; }

        /// <summary>
        /// The per-model rules: alias and on-detection policy, keyed by model name.
        /// </summary>
        public List<ModelRule> Models { get; set; } = new List<ModelRule>();

        /// <summary>
        /// Read-only landing spot for the pre-v0.8 name of <see cref="Models"/>, back
        /// when a rule mapped a model to a TCP port (#126 retired that). Deserialize-only
        /// — <see cref="ShouldSerializePortMappings"/> keeps it out of what we write, so
        /// the old key disappears on the next save. Without this an existing config file
        /// would load with no models at all: the schema version is unchanged (still 2),
        /// so the migrator never sees it, and Newtonsoft silently ignores a property it
        /// doesn't know. Aliases are the one thing an upgrader cannot re-derive.
        /// </summary>
        public List<ModelRule> PortMappings
        {
            get { return null; }
            set { if (value != null && value.Count > 0 && (Models == null || Models.Count == 0)) Models = value; }
        }

        public bool ShouldSerializePortMappings() => false;

        /// <summary>
        /// Crash anchors for serve sessions (#57): present only while a session is
        /// active (or after a PBIRelay crash mid-session). Absent in pre-v0.5 config
        /// files, which load unchanged.
        /// </summary>
        public List<ServeRecoveryRecord> ServeRecoveryRecords { get; set; } = new List<ServeRecoveryRecord>();

        /// <summary>
        /// XMLA-over-HTTP bridge settings (#77). Absent in pre-v0.7.2 config files,
        /// which load with the bridge disabled.
        /// </summary>
        public HttpBridgeConfig HttpBridge { get; set; } = new HttpBridgeConfig();
    }
}