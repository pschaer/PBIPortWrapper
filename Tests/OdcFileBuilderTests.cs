using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>Covers the .odc (Office Data Connection) file generation (#86).</summary>
    public sealed class OdcFileBuilderTests
    {
        [Fact]
        public void Build_embeds_the_full_connection_string()
        {
            var odc = OdcFileBuilder.Build("Sales", "localhost", 55555, "Sales");
            Assert.Contains(
                "<odc:ConnectionString>Provider=MSOLAP;Data Source=localhost:55555;Initial Catalog=Sales</odc:ConnectionString>",
                odc);
        }

        [Fact]
        public void Build_uses_cube_command_with_the_model_cube()
        {
            var odc = OdcFileBuilder.Build("Sales", "localhost", 55555, "Sales");
            Assert.Contains("<odc:CommandType>Cube</odc:CommandType>", odc);
            Assert.Contains("<odc:CommandText>Model</odc:CommandText>", odc);
            Assert.Contains("content=\"Model\"", odc); // Table meta
        }

        [Fact]
        public void Build_sets_catalog_meta_and_title()
        {
            var odc = OdcFileBuilder.Build("Sales Report", "localhost", 55555, "SalesCatalog");
            Assert.Contains("content=\"SalesCatalog\"", odc); // Catalog meta
            Assert.Contains("<title>Sales Report</title>", odc);
            Assert.Contains("ProgId", odc);
            Assert.Contains("text/x-ms-odc", odc);
        }

        [Fact]
        public void Build_title_falls_back_to_catalog_when_model_name_blank()
        {
            var odc = OdcFileBuilder.Build("  ", "localhost", 55555, "Finance");
            Assert.Contains("<title>Finance</title>", odc);
        }

        [Fact]
        public void Build_uses_lan_host_and_port_in_connection_string()
        {
            var odc = OdcFileBuilder.Build("Sales", "192.168.0.10", 55600, "Sales");
            Assert.Contains("Data Source=192.168.0.10:55600", odc);
        }

        [Theory]
        [InlineData("A&B")]
        [InlineData("Q<1>")]
        [InlineData("He said \"hi\"")]
        public void Build_xml_escapes_values(string modelName)
        {
            var odc = OdcFileBuilder.Build(modelName, "localhost", 55555, "Sales");
            Assert.DoesNotContain("<title>" + modelName + "</title>", odc);
            Assert.DoesNotContain("&B</title>", odc.Substring(odc.IndexOf("<title>"))); // raw & not left bare
        }

        [Fact]
        public void Build_honors_a_custom_cube_name()
        {
            var odc = OdcFileBuilder.Build("Sales", "localhost", 55555, "Sales", "SalesCube");
            Assert.Contains("<odc:CommandText>SalesCube</odc:CommandText>", odc);
        }

        [Fact]
        public void SuggestFileName_adds_extension()
        {
            Assert.Equal("Sales.odc", OdcFileBuilder.SuggestFileName("Sales"));
        }

        [Theory]
        [InlineData("Q1/Q2:Sales*", "Q1_Q2_Sales_.odc")]
        [InlineData("a\\b|c?", "a_b_c_.odc")]
        public void SuggestFileName_strips_invalid_characters(string input, string expected)
        {
            Assert.Equal(expected, OdcFileBuilder.SuggestFileName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SuggestFileName_defaults_when_blank(string input)
        {
            Assert.Equal("model.odc", OdcFileBuilder.SuggestFileName(input));
        }
    }
}
