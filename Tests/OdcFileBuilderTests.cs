using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    /// <summary>Covers the .odc (Office Data Connection) file generation (#86).</summary>
    public sealed class OdcFileBuilderTests
    {
        [Fact]
        public void Build_embeds_the_full_connection_string()
        {
            var odc = OdcFileBuilder.Build("Sales", "http://localhost:55556/Sales", "Sales");
            Assert.Contains(
                "<odc:ConnectionString>Provider=MSOLAP;Data Source=http://localhost:55556/Sales;Initial Catalog=Sales</odc:ConnectionString>",
                odc);
        }

        [Fact]
        public void Build_uses_cube_command_with_the_model_cube()
        {
            var odc = OdcFileBuilder.Build("Sales", "http://localhost:55556/Sales", "Sales");
            Assert.Contains("<odc:CommandType>Cube</odc:CommandType>", odc);
            Assert.Contains("<odc:CommandText>Model</odc:CommandText>", odc);
            Assert.Contains("content=\"Model\"", odc); // Table meta
        }

        [Fact]
        public void Build_sets_catalog_meta_and_title()
        {
            var odc = OdcFileBuilder.Build("Sales Report", "http://localhost:55556/SalesCatalog", "SalesCatalog");
            Assert.Contains("content=\"SalesCatalog\"", odc); // Catalog meta
            Assert.Contains("<title>Sales Report</title>", odc);
            Assert.Contains("ProgId", odc);
            Assert.Contains("text/x-ms-odc", odc);
        }

        [Fact]
        public void Build_title_falls_back_to_catalog_when_model_name_blank()
        {
            var odc = OdcFileBuilder.Build("  ", "http://localhost:55556/Finance", "Finance");
            Assert.Contains("<title>Finance</title>", odc);
        }

        [Fact]
        public void Build_uses_the_models_endpoint_url_as_the_data_source()
        {
            var odc = OdcFileBuilder.Build("Sales", "http://192.168.0.10:55556/Sales", "Sales");
            Assert.Contains("Data Source=http://192.168.0.10:55556/Sales", odc);
        }

        [Theory]
        [InlineData("A&B")]
        [InlineData("Q<1>")]
        [InlineData("He said \"hi\"")]
        public void Build_xml_escapes_values(string modelName)
        {
            var odc = OdcFileBuilder.Build(modelName, "http://localhost:55556/Sales", "Sales");
            Assert.DoesNotContain("<title>" + modelName + "</title>", odc);
            Assert.DoesNotContain("&B</title>", odc.Substring(odc.IndexOf("<title>"))); // raw & not left bare
        }

        [Fact]
        public void Build_honors_a_custom_cube_name()
        {
            var odc = OdcFileBuilder.Build("Sales", "http://localhost:55556/Sales", "Sales", "SalesCube");
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
