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
                PortMappings =
                {
                    new PortMappingRule { ModelNamePattern = "A", AutoConnect = true },
                    new PortMappingRule { ModelNamePattern = "B", AutoServe = true },
                    new PortMappingRule { ModelNamePattern = "C" }
                }
            };

            bool changed = ConfigMigrator.Migrate(config);

            Assert.True(changed);
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
            Assert.Equal(OnDetectionPolicy.Forward, config.PortMappings[0].OnDetection);
            Assert.Equal(OnDetectionPolicy.ServeImmediately, config.PortMappings[1].OnDetection);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.PortMappings[2].OnDetection);
        }

        [Fact]
        public void Migrate_is_idempotent()
        {
            var config = new ProxyConfiguration
            {
                ConfigVersion = 0,
                PortMappings = { new PortMappingRule { ModelNamePattern = "A", AutoConnect = true } }
            };

            Assert.True(ConfigMigrator.Migrate(config));   // first upgrades
            Assert.False(ConfigMigrator.Migrate(config));  // second is a no-op
            Assert.Equal(OnDetectionPolicy.Forward, config.PortMappings[0].OnDetection);
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
                PortMappings =
                {
                    new PortMappingRule
                    {
                        ModelNamePattern = "A",
                        AutoConnect = true,
                        OnDetection = OnDetectionPolicy.DoNothing
                    }
                }
            };

            bool changed = ConfigMigrator.Migrate(config);

            Assert.False(changed);
            Assert.Equal(OnDetectionPolicy.DoNothing, config.PortMappings[0].OnDetection);
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
            Assert.Equal(OnDetectionPolicy.Forward, config.PortMappings[0].OnDetection);
            Assert.Equal(OnDetectionPolicy.ServeImmediately, config.PortMappings[1].OnDetection);
        }

        [Fact]
        public void LoadConfiguration_missing_file_returns_current_version_config()
        {
            var config = new ConfigurationManager(_tempDir).LoadConfiguration();

            Assert.NotNull(config);
            Assert.Equal(ConfigMigrator.CurrentVersion, config.ConfigVersion);
            Assert.Empty(config.PortMappings);
        }
    }
}
