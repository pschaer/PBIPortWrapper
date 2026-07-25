using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Idempotent, forward-only config migrations keyed off
    /// <see cref="ProxyConfiguration.ConfigVersion"/> (#84). Run once on load
    /// (<see cref="ConfigurationManager.LoadConfiguration"/>); safe to run repeatedly.
    /// </summary>
    public static class ConfigMigrator
    {
        /// <summary>The version a fully-migrated config carries.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Upgrades <paramref name="config"/> in place to <see cref="CurrentVersion"/>.
        /// Returns true if anything changed (so callers may choose to persist it).
        /// </summary>
        public static bool Migrate(ProxyConfiguration config)
        {
            if (config == null) return false;

            bool changed = false;

            // v0 -> v1: derive the per-rule OnDetection policy from the legacy
            // AutoConnect/AutoServe booleans. Only touches configs that predate the
            // policy field; a saved v1 config keeps its explicit policies.
            if (config.ConfigVersion < 1)
            {
                if (config.PortMappings != null)
                {
                    foreach (var rule in config.PortMappings)
                    {
                        if (rule == null) continue;
                        rule.OnDetection = HostStateMachine.FromLegacy(rule.AutoConnect, rule.AutoServe);
                    }
                }

                config.ConfigVersion = 1;
                changed = true;
            }

            return changed;
        }
    }
}
