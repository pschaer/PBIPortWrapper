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
        public void UrlAclCommand_NamesTheRootPrefixForThePort()
        {
            // The root, not /xmla/ - a URL ACL for the old prefix does not cover it,
            // and that mismatch silently costs LAN access.
            Assert.Equal("netsh http add urlacl url=http://+:55556/ user=Everyone",
                EndpointUrlBuilder.UrlAclCommand(55556));
        }

        [Fact]
        public void EveryAuthMode_HasALabelAndAConsequence()
        {
            Assert.Equal(3, BridgeAuthModeLabel.Order.Count);

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
        public void AuthModeOrder_CoversEveryMode()
        {
            // A mode missing from Order would be invisible in every surface.
            foreach (BridgeAuthMode mode in Enum.GetValues<BridgeAuthMode>())
                Assert.Contains(mode, BridgeAuthModeLabel.Order);
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
        public void TheStatusLine_NamesReachabilityOnlyWhenItIsRestricted()
        {
            // The endpoint always binds every interface, so being reachable is what
            // running means. Announcing "LAN" every time says nothing and collides with
            // forwarding's per-model "Allow network access"; the fallback is the news.
            var reachable = new EndpointStatus(
                enabled: true, running: true, port: 55556, authMode: BridgeAuthMode.Basic);
            var localOnly = new EndpointStatus(
                enabled: true, running: true, port: 55556, authMode: BridgeAuthMode.Basic,
                isLocalOnly: true);

            Assert.DoesNotContain("LAN", reachable.Summary);
            Assert.DoesNotContain("this machine only", reachable.Summary);
            Assert.Contains("this machine only", localOnly.Summary);
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
