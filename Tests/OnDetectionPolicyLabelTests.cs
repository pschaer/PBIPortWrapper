using System;
using PBIPortWrapper.Models;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// The shared OnDetection labels back the tray submenu and the grid dropdown (#88),
    /// so the label/parse round-trip must be exact for every policy.
    /// </summary>
    public class OnDetectionPolicyLabelTests
    {
        [Theory]
        [InlineData(OnDetectionPolicy.DoNothing, "Do nothing")]
        [InlineData(OnDetectionPolicy.Forward, "Forward")]
        [InlineData(OnDetectionPolicy.ServeImmediately, "Serve")]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, "Serve after grace period")]
        public void For_matches_expected_label(OnDetectionPolicy policy, string label) =>
            Assert.Equal(label, OnDetectionPolicyLabel.For(policy));

        [Fact]
        public void Every_policy_round_trips_through_its_label()
        {
            foreach (OnDetectionPolicy policy in Enum.GetValues(typeof(OnDetectionPolicy)))
            {
                Assert.True(OnDetectionPolicyLabel.TryParse(OnDetectionPolicyLabel.For(policy), out var parsed));
                Assert.Equal(policy, parsed);
            }
        }

        [Fact]
        public void Order_lists_every_policy_once()
        {
            var all = (OnDetectionPolicy[])Enum.GetValues(typeof(OnDetectionPolicy));
            Assert.Equal(all.Length, OnDetectionPolicyLabel.Order.Length);
            Assert.Equal(all.Length, new System.Collections.Generic.HashSet<OnDetectionPolicy>(OnDetectionPolicyLabel.Order).Count);
        }

        [Fact]
        public void TryParse_unknown_label_is_false()
        {
            Assert.False(OnDetectionPolicyLabel.TryParse("nonsense", out var policy));
            Assert.Equal(OnDetectionPolicy.DoNothing, policy);
        }
    }
}
