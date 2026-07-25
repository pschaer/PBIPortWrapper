using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers model-name extraction from the Desktop window title (#94), which lost
    /// its " - Power BI Desktop" suffix in the July 2026 build.
    /// </summary>
    public sealed class WindowTitleParserTests
    {
        [Theory]
        [InlineData("Sample01 - Power BI Desktop", "Sample01")]   // legacy format
        [InlineData("Sample01", "Sample01")]                       // July 2026 format
        [InlineData("Sample01_emptyTest", "Sample01_emptyTest")]
        [InlineData("My Report - Power BI Desktop", "My Report")]
        [InlineData("  Sample01  ", "Sample01")]                    // trimmed
        public void ModelName_extracts_the_name(string title, string expected)
        {
            Assert.Equal(expected, WindowTitleParser.ModelName(title));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" - Power BI Desktop")]   // suffix only -> no name
        public void ModelName_returns_null_when_blank(string title)
        {
            Assert.Null(WindowTitleParser.ModelName(title));
        }
    }
}
