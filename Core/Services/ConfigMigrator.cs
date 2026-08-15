using PBIRelay.Models;

namespace PBIRelay.Services
{
    /// <summary>
    /// Idempotent, forward-only config migrations keyed off
    /// <see cref="ProxyConfiguration.ConfigVersion"/> (#84). Run once on load
    /// (<see cref="ConfigurationManager.LoadConfiguration"/>); safe to run repeatedly.
    /// </summary>
    public static class ConfigMigrator
    {
        /// <summary>The version a fully-migrated config carries.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// The integer <c>OnDetectionPolicy.Forward</c> used to persist as. The member
        /// is gone, so a config carrying it deserializes to an enum value with no name
        /// and has to be recognised numerically.
        /// </summary>
        private const int RetiredForwardPolicy = 1;

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
                if (config.Models != null)
                {
                    foreach (var rule in config.Models)
                    {
                        if (rule == null) continue;
                        rule.OnDetection = HostStateMachine.FromLegacy(rule.AutoConnect, rule.AutoServe);
                    }
                }

                config.ConfigVersion = 1;
                changed = true;
            }

            // v1 -> v2: forwarding is gone (#126). A rule set to forward on detection
            // has no successor that does the same thing, so it becomes "do nothing".
            //
            // Deliberately NOT promoted to serving: serving renames the database and
            // blocks editing in Desktop, and doing that to someone's models because
            // they once ticked a box for a different feature would be a nasty
            // surprise on first launch. They opt back in, per model, once.
            //
            // Nothing else about the rule is touched — the alias above all, which is
            // the identity a served model is addressed by and the one thing that must
            // survive this release.
            if (config.ConfigVersion < 2)
            {
                if (config.Models != null)
                {
                    foreach (var rule in config.Models)
                    {
                        if (rule == null) continue;
                        if ((int)rule.OnDetection == RetiredForwardPolicy)
                            rule.OnDetection = OnDetectionPolicy.DoNothing;
                    }
                }

                config.ConfigVersion = 2;
                changed = true;
            }

            return changed;
        }
    }
}
