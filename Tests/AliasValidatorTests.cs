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
    }
}
