using PBIPortWrapper.Models;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class RenameResultTests
    {
        [Fact]
        public void Fail_SetsSuccessFalse_AndMessage()
        {
            var result = RenameResult.Fail("boom");

            Assert.False(result.Success);
            Assert.Equal("boom", result.Message);
            Assert.Null(result.WarningMessage);
        }

        [Fact]
        public void Ok_WithoutWarning_HasNoWarning()
        {
            var result = RenameResult.Ok("done");

            Assert.True(result.Success);
            Assert.Equal("done", result.Message);
            Assert.Null(result.WarningMessage);
        }

        [Fact]
        public void Ok_WithWarning_CarriesWarning()
        {
            var result = RenameResult.Ok("done", "careful");

            Assert.True(result.Success);
            Assert.Equal("careful", result.WarningMessage);
        }
    }
}
