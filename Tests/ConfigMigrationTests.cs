using System;
using System.IO;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the config schema migration (#84): pre-v0.7 files (no ConfigVersion,
    /// no OnDetection) get their per-rule policy derived from the legacy
    /// AutoConnect/AutoServe booleans, once and idempotently, including through
    /// ConfigurationManager.LoadConfiguration.
    /// </summary>
    public sealed class ConfigMigrationTests : IDisposable
    {
        private readonly string _tempDir;

        public ConfigMigrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "pbipw-migtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void Migrate_v0_derives_policy_and_bumps_version()
        {
            var config = new ProxyConfiguration
            {
                ConfigVersion = 0,
                Models =
                {
                    new ModelRule { ModelNamePattern = "A", AutoConnect = true },
                    new ModelRule { ModelNamePattern = "B", AutoServe = true },
                    new ModelRule { ModelNamePattern = "C" }
                }
            };

            bool changed = ConfigMigrator.Migrate(config);

            Assert.True(changed);
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[0].OnDetection);   // #126: forwarding is gone
            Assert.Equal(OnDetectionPolicy.ServeImmediately, config.Models[1].OnDetection);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[2].OnDetection);
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var config = new ProxyConfiguration
            {
                ConfigVersion = 0,
                Models = { new ModelRule { ModelNamePattern = "A", AutoConnect = true } }
            };

            Assert.True(ConfigMigrator.Migrate(config));   // first upgrades
            Assert.False(ConfigMigrator.Migrate(config));  // second is a no-op
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[0].OnDetection);   // #126: forwarding is gone
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
        }

        [Fact]
        public void Migrate_does_not_override_explicit_policy_in_current_version_config()
        {
            // A v1 config where the user deliberately chose DoNothing despite a
            // legacy AutoConnect=true must keep its explicit policy.
            var config = new ProxyConfiguration
            {
                ConfigVersion = ConfigMigrator.CurrentVersion,
                Models =
                {
                    new ModelRule
                    {
                        ModelNamePattern = "A",
                        AutoConnect = true,
                        OnDetection = OnDetectionPolicy.DoNothing
                    }
                }
            };

            bool changed = ConfigMigrator.Migrate(config);

            Assert.False(changed);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[0].OnDetection);
        }

        [Fact]
        public void Migrate_null_config_returns_false()
        {
            Assert.False(ConfigMigrator.Migrate(null));
        }

        [Fact]
        public void LoadConfiguration_migrates_legacy_file()
        {
            // A pre-v0.7 config.json: no ConfigVersion, no OnDetection, just the
            // legacy booleans. Written raw so we exercise real deserialization.
            var legacyJson =
                "{ \"PortMappings\": [" +
                "  { \"ModelNamePattern\": \"Sales\", \"AutoConnect\": true }," +
                "  { \"ModelNamePattern\": \"Finance\", \"AutoServe\": true }" +
                "] }";
            File.WriteAllText(Path.Combine(_tempDir, "config.json"), legacyJson);

            var config = new ConfigurationManager(_tempDir).LoadConfiguration();

            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[0].OnDetection);   // #126: forwarding is gone
            Assert.Equal(OnDetectionPolicy.ServeImmediately, config.Models[1].OnDetection);
        }

        [Fact]
        public void LoadConfiguration_missing_file_returns_current_version_config()
        {
            var config = new ConfigurationManager(_tempDir).LoadConfiguration();

            Assert.NotNull(config);
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
            Assert.Empty(config.Models);
        }

        // ---- v1 -> v2: forwarding retired (#126) ----

        [Fact]
        public void V1_forward_policy_becomes_do_nothing()
        {
            // Value 1 was OnDetectionPolicy.Forward. The member is gone, so a config
            // written by v0.7.x carries an enum value with no name and has to be
            // recognised numerically.
            var config = new ProxyConfiguration
            {
                ConfigVersion = 1,
                Models = { new ModelRule { ModelNamePattern = "Sales", OnDetection = (OnDetectionPolicy)1 } }
            };

            Assert.True(ConfigMigrator.Migrate(config));

            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[0].OnDetection);
            Assert.Equal(2, config.ConfigVersion);
        }

        [Fact]
        public void V1_forward_policy_is_not_promoted_to_serving()
        {
            // Serving renames the database and blocks editing in Desktop. Doing that
            // on first launch to someone who ticked a box for a different feature
            // would be a nasty surprise; they opt back in per model.
            var config = new ProxyConfiguration
            {
                ConfigVersion = 1,
                Models = { new ModelRule { ModelNamePattern = "Sales", OnDetection = (OnDetectionPolicy)1 } }
            };

            ConfigMigrator.Migrate(config);

            Assert.NotEqual(OnDetectionPolicy.ServeImmediately, config.Models[0].OnDetection);
            Assert.NotEqual(OnDetectionPolicy.ServeAfterGrace, config.Models[0].OnDetection);
        }

        [Fact]
        public void Migration_never_loses_a_stable_alias()
        {
            // The alias is the identity a served model is addressed by, and the one
            // thing an upgrading user cannot re-derive. It must survive every hop.
            var config = new ProxyConfiguration
            {
                ConfigVersion = 0,
                Models =
                {
                    new ModelRule { ModelNamePattern = "Sales", StableAlias = "Sales Model", AutoConnect = true },
                    new ModelRule { ModelNamePattern = "Finance", StableAlias = "Finance 2026", AutoServe = true }
                }
            };

            ConfigMigrator.Migrate(config);

            Assert.Equal("Sales Model", config.Models[0].StableAlias);
            Assert.Equal("Finance 2026", config.Models[1].StableAlias);
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
        }

        [Fact]
        public void Explicit_serve_policies_survive_the_v2_migration()
        {
            var config = new ProxyConfiguration
            {
                ConfigVersion = 1,
                Models =
                {
                    new ModelRule { ModelNamePattern = "A", OnDetection = OnDetectionPolicy.ServeImmediately },
                    new ModelRule { ModelNamePattern = "B", OnDetection = OnDetectionPolicy.ServeAfterGrace },
                    new ModelRule { ModelNamePattern = "C", OnDetection = OnDetectionPolicy.DoNothing }
                }
            };

            ConfigMigrator.Migrate(config);

            Assert.Equal(OnDetectionPolicy.ServeImmediately, config.Models[0].OnDetection);
            Assert.Equal(OnDetectionPolicy.ServeAfterGrace, config.Models[1].OnDetection);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.Models[2].OnDetection);
        }
    }
}
