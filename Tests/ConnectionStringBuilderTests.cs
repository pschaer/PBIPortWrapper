using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>Covers the connection-string formatting helpers (#85).</summary>
    public sealed class ConnectionStringBuilderTests
    {
        [Fact]
        public void DataSource_is_host_colon_port()
        {
            Assert.Equal("localhost:55555", ConnectionStringBuilder.DataSource("localhost", 55555));
        }

        [Fact]
        public void Full_without_alias_omits_initial_catalog()
        {
            Assert.Equal("Provider=MSOLAP;Data Source=localhost:55555",
                ConnectionStringBuilder.Full("localhost", 55555));
        }

        [Fact]
        public void Full_with_alias_includes_initial_catalog()
        {
            Assert.Equal("Provider=MSOLAP;Data Source=192.168.0.10:55555;Initial Catalog=Sales",
                ConnectionStringBuilder.Full("192.168.0.10", 55555, "Sales"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Full_treats_blank_alias_as_no_catalog(string alias)
        {
            Assert.Equal("Provider=MSOLAP;Data Source=localhost:55555",
                ConnectionStringBuilder.Full("localhost", 55555, alias));
        }
    }
}
