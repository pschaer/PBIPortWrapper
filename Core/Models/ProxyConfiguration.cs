using System.Collections.Generic;

namespace PBIPortWrapper.Models
{
    // Top-level FixedPort/AllowNetworkAccess were dead v0.1 leftovers (#59); the
    // real values live per-rule in PortMappingRule. Newtonsoft ignores unknown
    // members, so old config files containing them still load.
    public class ProxyConfiguration
    {
        public bool MinimizeToTray { get; set; } = false;
        public string LastSelectedInstance { get; set; }
        public List<PortMappingRule> PortMappings { get; set; } = new List<PortMappingRule>();

        /// <summary>
        /// Crash anchors for serve sessions (#57): present only while a session is
        /// active (or after a wrapper crash mid-session). Absent in pre-v0.5 config
        /// files, which load unchanged.
        /// </summary>
        public List<ServeRecoveryRecord> ServeRecoveryRecords { get; set; } = new List<ServeRecoveryRecord>();
    }
}