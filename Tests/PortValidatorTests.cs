using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class PortValidatorTests
    {
        [Theory]
        [InlineData("55555", true, 55555)]
        [InlineData("1", true, 1)]
        [InlineData("65535", true, 65535)]
        [InlineData("0", false, 0)]
        [InlineData("65536", false, 65536)]
        [InlineData("-1", false, -1)]
        [InlineData("abc", false, 0)]
        [InlineData("", false, 0)]
        public void TryParsePort_EnforcesRange(string input, bool expectedValid, int expectedPort)
        {
            bool valid = PortValidator.TryParsePort(input, out int port);

            Assert.Equal(expectedValid, valid);
            Assert.Equal(expectedPort, port);
        }

        [Fact]
        public void ValidatePortAssignment_EmptyIsAllowed()
        {
            var (isValid, error) = PortValidator.ValidatePortAssignment("", new int[0]);

            Assert.True(isValid);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void ValidatePortAssignment_NonNumeric_Fails()
        {
            var (isValid, error) = PortValidator.ValidatePortAssignment("abc", new int[0]);

            Assert.False(isValid);
            Assert.Equal("Port must be a number", error);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        public void ValidatePortAssignment_OutOfRange_Fails(string input)
        {
            var (isValid, error) = PortValidator.ValidatePortAssignment(input, new int[0]);

            Assert.False(isValid);
            Assert.Equal("Port must be between 1 and 65535", error);
        }

        [Fact]
        public void ValidatePortAssignment_Duplicate_Fails()
        {
            var (isValid, error) = PortValidator.ValidatePortAssignment("55555", new[] { 50000, 55555 });

            Assert.False(isValid);
            Assert.Contains("already assigned", error);
        }

        [Fact]
        public void ValidatePortAssignment_UniquePort_Succeeds()
        {
            var (isValid, _) = PortValidator.ValidatePortAssignment("55556", new[] { 50000, 55555 });

            Assert.True(isValid);
        }
    }
}
