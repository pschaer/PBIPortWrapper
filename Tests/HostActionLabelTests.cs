using PBIRelay.Models;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class HostActionLabelTests
    {
        [Theory]
        [InlineData(HostAction.Serve, "Serve")]
        [InlineData(HostAction.Stop, "Stop")]
        public void For_matches_expected_label(HostAction action, string label) =>
            Assert.Equal(label, HostActionLabel.For(action));
    }
}
