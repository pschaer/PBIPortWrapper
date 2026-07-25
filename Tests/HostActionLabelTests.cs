using PBIPortWrapper.Models;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class HostActionLabelTests
    {
        [Theory]
        [InlineData(HostAction.Forward, "Forward")]
        [InlineData(HostAction.Serve, "Serve")]
        [InlineData(HostAction.Stop, "Stop")]
        [InlineData(HostAction.StopServing, "Stop")]
        public void For_matches_expected_label(HostAction action, string label) =>
            Assert.Equal(label, HostActionLabel.For(action));
    }
}
