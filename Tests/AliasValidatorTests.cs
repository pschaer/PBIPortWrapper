using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class AliasValidatorTests
    {
        [Theory]
        [InlineData("Sales")]
        [InlineData("Sales Model 2026")]
        [InlineData("sales_model-v2.1")]
        public void ValidAliases_Pass(string alias)
        {
            var (isValid, errorMessage) = AliasValidator.ValidateAlias(alias);

            Assert.True(isValid);
            Assert.Equal(string.Empty, errorMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyAlias_Fails(string? alias)
        {
            var (isValid, errorMessage) = AliasValidator.ValidateAlias(alias!);

            Assert.False(isValid);
            Assert.Contains("empty", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AliasOverMaxLength_Fails()
        {
            var tooLong = new string('a', AliasValidator.MaxLength + 1);

            var (isValid, errorMessage) = AliasValidator.ValidateAlias(tooLong);

            Assert.False(isValid);
            Assert.Contains("too long", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AliasAtMaxLength_Passes()
        {
            var (isValid, _) = AliasValidator.ValidateAlias(new string('a', AliasValidator.MaxLength));

            Assert.True(isValid);
        }

        [Theory]
        [InlineData("Sales<Model>")]
        [InlineData("Sales/2026")]
        [InlineData("Sales;DROP")]
        [InlineData("Sales\"Quote")]
        public void InvalidCharacters_Fail(string alias)
        {
            var (isValid, errorMessage) = AliasValidator.ValidateAlias(alias);

            Assert.False(isValid);
            Assert.Contains("invalid characters", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Sales/2026")]      // a path separator: unwritable as one segment
        [InlineData("Sales?live")]      // starts a query string
        [InlineData("Sales#1")]         // starts a fragment, never sent to the server
        [InlineData("Sales%2026")]      // reads as percent-encoding
        [InlineData("Sales&Ops")]
        public void CharactersThatWouldBreakAModelUrl_AreRejected(string alias)
        {
            // An alias is also the URL path a client addresses the model on (#136).
            // These characters cannot survive that round trip, so widening the
            // permitted set to admit them would break addressing rather than the name.
            var (isValid, _) = AliasValidator.ValidateAlias(alias);

            Assert.False(isValid);
        }

        [Fact]
        public void AnAliasWithASpace_SurvivesTheUrlRoundTrip()
        {
            // Space is the one permitted character a URL cannot carry literally, and it
            // is the reason the relay decodes the path rather than comparing it raw.
            const string alias = "Sales Model 2026";
            Assert.True(AliasValidator.ValidateAlias(alias).IsValid);

            Assert.Equal(alias, XmlaRelay.ModelFromPath("/" + Uri.EscapeDataString(alias)));
        }
    }
}
