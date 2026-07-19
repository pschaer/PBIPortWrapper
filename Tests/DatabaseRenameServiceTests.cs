using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the validation paths of RenameDatabaseAsync, which return before any
    /// server connection is attempted. Connected behavior is covered by experiments
    /// E2/E3 against a live Power BI Desktop instance, not unit tests.
    /// </summary>
    public class DatabaseRenameServiceTests
    {
        private sealed class NullLogger : ILogger
        {
            public void Log(LogLevel level, string category, string message, Exception? exception = null) { }
            public string GetLogFilePath() => string.Empty;
        }

        private readonly DatabaseRenameService _service = new(new NullLogger());

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Rename_EmptyName_Fails(string? newName)
        {
            var result = await _service.RenameDatabaseAsync(55555, "current", newName!);

            Assert.False(result.Success);
            Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Rename_NameOver100Chars_Fails()
        {
            var tooLong = new string('a', 101);

            var result = await _service.RenameDatabaseAsync(55555, "current", tooLong);

            Assert.False(result.Success);
            Assert.Contains("too long", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Sales<Model>")]
        [InlineData("Sales/2026")]
        [InlineData("Sales;DROP")]
        [InlineData("Sales\"Quote")]
        public async Task Rename_InvalidCharacters_Fails(string newName)
        {
            var result = await _service.RenameDatabaseAsync(55555, "current", newName);

            Assert.False(result.Success);
            Assert.Contains("invalid characters", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Sales")]
        [InlineData("Sales Model 2026")]
        [InlineData("sales_model-v2.1")]
        public void ValidAliasShapes_AreAcceptedByTheSharedRules(string name)
        {
            // The service delegates name validation to AliasValidator (shared rules).
            Assert.True(AliasValidator.ValidateAlias(name).IsValid);
        }
    }
}
