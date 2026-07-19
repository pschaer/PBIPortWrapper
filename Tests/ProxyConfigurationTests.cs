using Newtonsoft.Json;
using PBIPortWrapper.Models;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class ProxyConfigurationTests
    {
        [Fact]
        public void Defaults_MatchDocumentedBehavior()
        {
            var config = new ProxyConfiguration();

            Assert.False(config.MinimizeToTray);
            Assert.NotNull(config.PortMappings);
            Assert.Empty(config.PortMappings);
        }

        [Fact]
        public void JsonRoundTrip_PreservesPortMappings()
        {
            var config = new ProxyConfiguration
            {
                PortMappings =
                {
                    new PortMappingRule("Sales.pbix", 55555, autoConnect: true, allowNetworkAccess: false)
                    {
                        StableAlias = "Sales",
                        AutoServe = true
                    }
                }
            };

            var json = JsonConvert.SerializeObject(config);
            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>(json);

            Assert.NotNull(restored);
            var rule = Assert.Single(restored!.PortMappings);
            Assert.Equal("Sales.pbix", rule.ModelNamePattern);
            Assert.Equal(55555, rule.FixedPort);
            Assert.True(rule.AutoConnect);
            Assert.False(rule.AllowNetworkAccess);
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
            var rule = Assert.Single(restored!.PortMappings);
            Assert.Equal("Sales", rule.StableAlias);
            Assert.False(rule.AutoServe); // absent in old configs -> off
        }

        [Fact]
        public void Serialize_WritesStableAliasAsRenamedDatabaseName()
        {
            var rule = new PortMappingRule { StableAlias = "Sales" };

            var json = JsonConvert.SerializeObject(rule);

            Assert.Contains("\"RenamedDatabaseName\":\"Sales\"", json);
            Assert.DoesNotContain("StableAlias", json);
        }

        [Fact]
        public void Deserialize_EmptyObject_AppliesDefaults()
        {
            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>("{}");

            Assert.NotNull(restored);
            Assert.NotNull(restored!.PortMappings);
        }
    }
}
