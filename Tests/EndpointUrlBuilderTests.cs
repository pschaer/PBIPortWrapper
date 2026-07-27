using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class EndpointUrlBuilderTests
    {
        [Fact]
        public void For_BuildsTheModelsOwnPath()
        {
            Assert.Equal("http://10.9.30.10:55556/Sales",
                EndpointUrlBuilder.For("10.9.30.10", 55556, "Sales"));
        }

        [Fact]
        public void For_EncodesAnAliasWithASpace_AndTheRelayDecodesItBack()
        {
            // The two halves of the same contract: what the tray hands a user must be
            // what the endpoint resolves.
            string url = EndpointUrlBuilder.For("host", 55556, "Sales Model 2026");

            Assert.Equal("http://host:55556/Sales%20Model%202026", url);
            Assert.Equal("Sales Model 2026", XmlaRelay.ModelFromPath("/Sales%20Model%202026"));
        }

        [Fact]
        public void For_TrimsTheAlias()
        {
            Assert.Equal("http://host:55556/Sales", EndpointUrlBuilder.For("host", 55556, "  Sales  "));
        }

        [Theory]
        [InlineData(null, 55556, "Sales")]
        [InlineData("", 55556, "Sales")]
        [InlineData("host", 0, "Sales")]
        [InlineData("host", 55556, null)]
        [InlineData("host", 55556, "   ")]
        public void For_WithoutEnoughToAddressAModel_ReturnsEmpty(string host, int port, string alias)
        {
            // Better nothing than a URL that cannot resolve.
            Assert.Equal(string.Empty, EndpointUrlBuilder.For(host, port, alias));
        }

        [Fact]
        public void EveryAuthMode_HasALabelAndAConsequence()
        {
            Assert.Equal(2, BridgeAuthModeLabel.Order.Count);

            foreach (BridgeAuthMode mode in BridgeAuthModeLabel.Order)
            {
                Assert.False(string.IsNullOrWhiteSpace(BridgeAuthModeLabel.For(mode)));
                Assert.False(string.IsNullOrWhiteSpace(BridgeAuthModeLabel.Describe(mode)));
            }
        }

        [Fact]
        public void AuthModeLabels_AreDistinct()
        {
            // Two modes reading the same in a menu would be unpickable.
            Assert.Equal(
                BridgeAuthModeLabel.Order.Count,
                new HashSet<string>(BridgeAuthModeLabel.Order.Select(BridgeAuthModeLabel.For)).Count);
        }

        [Fact]
        public void AuthModeOrder_OffersEveryModeThatWorks()
        {
            // A mode missing from Order is invisible in every surface, which is the
            // point for Windows and a bug for anything else.
            Assert.Contains(BridgeAuthMode.Basic, BridgeAuthModeLabel.Order);
            Assert.Contains(BridgeAuthMode.Anonymous, BridgeAuthModeLabel.Order);
        }

        [Fact]
        public void TheRetiredWindowsMode_IsNotOffered_ButCanStillBeNamed()
        {
            // Negotiate is no longer offered (#164) because it cannot work on a host
            // that is not domain-joined. The label survives so a config still carrying
            // it can be displayed rather than rendering as a bare enum or a blank.
            Assert.DoesNotContain(BridgeAuthMode.Windows, BridgeAuthModeLabel.Order);
            Assert.False(string.IsNullOrWhiteSpace(BridgeAuthModeLabel.For(BridgeAuthMode.Windows)));
        }

        [Theory]
        [InlineData(BridgeAuthMode.Basic)]
        [InlineData(BridgeAuthMode.Anonymous)]
        [InlineData(BridgeAuthMode.Windows)]
        public void TheStatusLine_NamesTheModeExactlyAsTheMenuDoes(BridgeAuthMode mode)
        {
            // The status line said "Basic" while the menu said "Username and password",
            // which reads as two different settings. One label, both places.
            var status = new EndpointStatus(
                enabled: true, running: true, port: 55556, authMode: mode);

            Assert.Contains(BridgeAuthModeLabel.For(mode), status.Summary);
        }

        [Fact]
        public void TheStatusLine_DoesNotAnnounceReachability()
        {
            // Kestrel binds every address without a URL reservation, so running IS
            // reachable and there is no longer a restricted case to name (#132).
            var status = new EndpointStatus(
                enabled: true, running: true, port: 55556, authMode: BridgeAuthMode.Basic);

            Assert.DoesNotContain("LAN", status.Summary);
            Assert.DoesNotContain("this machine only", status.Summary);
            Assert.True(status.IsLanReachable);
        }

        [Fact]
        public void TheStatusLine_DoesNotLeakEnumNames()
        {
            var status = new EndpointStatus(
                enabled: true, running: true, port: 55556, authMode: BridgeAuthMode.Basic);

            Assert.DoesNotContain("Basic", status.Summary);
        }
    }
}
