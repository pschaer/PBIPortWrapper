using System;
using System.IO;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the single-writer contract of ConfigService (#62): every mutation goes
    /// through the same in-memory Current and is persisted immediately, so no write
    /// path can clobber another's changes with a stale copy.
    /// </summary>
    public sealed class ConfigServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ConfigService _service;

        public ConfigServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PBIPortWrapperTests", Guid.NewGuid().ToString("N"));
            _service = new ConfigService(new ConfigurationManager(_tempDir));
            _service.Load();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private ConfigService ReloadFromDisk()
        {
            var fresh = new ConfigService(new ConfigurationManager(_tempDir));
            fresh.Load();
            return fresh;
        }

        [Fact]
        public void SetStableAlias_CreatesRuleAndPersists()
        {
            _service.SetStableAlias("Sample01", "Sales");

            var restored = ReloadFromDisk();
            var rule = Assert.Single(restored.Current.PortMappings);
            Assert.Equal("Sample01", rule.ModelNamePattern);
            Assert.Equal("Sales", rule.StableAlias);
        }

        [Fact]
        public void SetStableAlias_OnExistingRule_KeepsOtherFields()
        {
            _service.UpdateRule("Sample01", 55555, autoConnect: true, allowNetwork: true);

            _service.SetStableAlias("Sample01", "Sales");

            var rule = Assert.Single(ReloadFromDisk().Current.PortMappings);
            Assert.Equal(55555, rule.FixedPort);
            Assert.True(rule.AutoConnect);
            Assert.True(rule.AllowNetworkAccess);
            Assert.Equal("Sales", rule.StableAlias);
        }

        [Fact]
        public void UpdateRule_AfterSetStableAlias_PreservesAlias()
        {
            // Regression for #62: an alias saved by the rename panel was clobbered by
            // the next grid toggle saving a stale cached config. With a single Current
            // the toggle must preserve the alias.
            _service.UpdateRule("Sample01", 55555, autoConnect: true, allowNetwork: false);
            _service.SetStableAlias("Sample01", "Sales");

            _service.UpdateRule("Sample01", 55555, autoConnect: false, allowNetwork: true);

            var rule = Assert.Single(ReloadFromDisk().Current.PortMappings);
            Assert.Equal("Sales", rule.StableAlias);
            Assert.False(rule.AutoConnect);
            Assert.True(rule.AllowNetworkAccess);
        }

        [Fact]
        public void FindRule_MatchesExactModelName()
        {
            _service.UpdateRule("Sample01", 55555, autoConnect: false, allowNetwork: false);

            Assert.NotNull(_service.FindRule("Sample01"));
            Assert.Null(_service.FindRule("Sample"));
            Assert.Null(_service.FindRule("sample01")); // exact, case-sensitive as everywhere else
            Assert.Null(_service.FindRule(null));
        }

        [Fact]
        public void SetStableAlias_RefusesUntitled()
        {
            // #9: rules match by file name; a rule stored under "Untitled" is
            // orphaned as soon as the model is saved under its real name.
            _service.SetStableAlias("Untitled", "Sales");
            _service.SetStableAlias("untitled", "Sales");

            Assert.Empty(ReloadFromDisk().Current.PortMappings);
        }

        [Fact]
        public void SetStableAlias_DoesNotDuplicateRules()
        {
            _service.SetStableAlias("Sample01", "Sales");
            _service.SetStableAlias("Sample01", "SalesV2");
            _service.UpdateRule("Sample01", 55556, autoConnect: false, allowNetwork: false);

            var rule = Assert.Single(ReloadFromDisk().Current.PortMappings);
            Assert.Equal("SalesV2", rule.StableAlias);
            Assert.Equal(55556, rule.FixedPort);
        }

        [Fact]
        public void Load_MissingFile_YieldsDefaults()
        {
            Assert.NotNull(_service.Current);
            Assert.Empty(_service.Current.PortMappings);
        }
    }
}
