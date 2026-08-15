using Newtonsoft.Json;
using PBIRelay.Models;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class ProxyConfigurationTests
    {
        [Fact]
        public void Defaults_MatchDocumentedBehavior()
        {
            var config = new ProxyConfiguration();

            Assert.False(config.MinimizeToTray);
            Assert.NotNull(config.Models);
            Assert.Empty(config.Models);
        }

        [Fact]
        public void JsonRoundTrip_PreservesModels()
        {
            var config = new ProxyConfiguration
            {
                Models =
                {
                    new ModelRule("Sales.pbix", "Sales") { AutoConnect = true, AutoServe = true }
                }
            };

            var json = JsonConvert.SerializeObject(config);
            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>(json);

            Assert.NotNull(restored);
            var rule = Assert.Single(restored!.Models);
            Assert.Equal("Sales.pbix", rule.ModelNamePattern);
            Assert.True(rule.AutoConnect);
            Assert.Equal("Sales", rule.StableAlias);
            Assert.True(rule.AutoServe);
        }

        [Fact]
        public void Deserialize_PreV05Config_LoadsRenamedDatabaseNameIntoStableAlias()
        {
            // Exact shape a v0.3/v0.4 config.json would contain. The retired
            // top-level FixedPort/AllowNetworkAccess (#59) must be tolerated.
            var oldJson = @"{
                ""FixedPort"": 55555,
                ""AllowNetworkAccess"": false,
                ""PortMappings"": [
                    {
                        ""ModelNamePattern"": ""Sales\\.pbix"",
                        ""FixedPort"": 55556,
                        ""AutoConnect"": true,
                        ""AllowNetworkAccess"": false,
                        ""RenamedDatabaseName"": ""Sales""
                    }
                ]
            }";

            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>(oldJson);

            Assert.NotNull(restored);
            var rule = Assert.Single(restored!.Models);
            Assert.Equal("Sales", rule.StableAlias);
            Assert.False(rule.AutoServe); // absent in old configs -> off
        }

        [Fact]
        public void Serialize_WritesStableAliasAsRenamedDatabaseName()
        {
            var rule = new ModelRule { StableAlias = "Sales" };

            var json = JsonConvert.SerializeObject(rule);

            Assert.Contains("\"RenamedDatabaseName\":\"Sales\"", json);
            Assert.DoesNotContain("StableAlias", json);
        }

        [Fact]
        public void Deserialize_PreV08Config_LoadsPortMappingsIntoModels()
        {
            // v0.8 renamed the property to "Models" without bumping the schema version,
            // so nothing migrates this file - the legacy landing spot is the only thing
            // standing between an upgrader and losing every alias they set (#130).
            var oldJson = @"{
                ""ConfigVersion"": 2,
                ""PortMappings"": [
                    {
                        ""ModelNamePattern"": ""Sample01"",
                        ""RenamedDatabaseName"": ""Sales"",
                        ""OnDetection"": 3
                    }
                ]
            }";

            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>(oldJson);

            Assert.NotNull(restored);
            var rule = Assert.Single(restored!.Models);
            Assert.Equal("Sample01", rule.ModelNamePattern);
            Assert.Equal("Sales", rule.StableAlias);
            Assert.Equal(OnDetectionPolicy.ServeImmediately, rule.OnDetection);
        }

        [Fact]
        public void Serialize_WritesModelsAndDropsTheLegacyName()
        {
            var config = new ProxyConfiguration
            {
                Models = { new ModelRule("Sample01", "Sales") }
            };

            var json = JsonConvert.SerializeObject(config);

            Assert.Contains("\"Models\"", json);
            Assert.DoesNotContain("PortMappings", json);
        }

        [Fact]
        public void Deserialize_EmptyObject_AppliesDefaults()
        {
            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>("{}");

            Assert.NotNull(restored);
            Assert.NotNull(restored!.Models);
        }
    }
}
