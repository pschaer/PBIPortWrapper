using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class XmlaClientRegistryTests
    {
        [Fact]
        public void A_client_is_announced_once_then_stays_quiet()
        {
            var registry = new XmlaClientRegistry();

            Assert.True(registry.IsNew("MSOLAP", "PASCAL"));
            Assert.False(registry.IsNew("MSOLAP", "PASCAL"));
            Assert.False(registry.IsNew("MSOLAP", "PASCAL"));
        }

        [Fact]
        public void The_same_tool_as_a_different_caller_is_a_different_client()
        {
            // A compatibility pass runs one client across authentication modes, so the
            // account it arrives as is part of what identifies it.
            var registry = new XmlaClientRegistry();

            Assert.True(registry.IsNew("MSOLAP", "PASCAL"));
            Assert.True(registry.IsNew("MSOLAP", "anonymous"));
        }

        [Fact]
        public void Different_tools_are_announced_separately()
        {
            var registry = new XmlaClientRegistry();

            Assert.True(registry.IsNew("MSOLAP 16.0", "PASCAL"));
            Assert.True(registry.IsNew("DAX Studio", "PASCAL"));
            Assert.True(registry.IsNew(null, "PASCAL"));
        }

        [Fact]
        public void A_restart_announces_everything_again()
        {
            var registry = new XmlaClientRegistry();
            registry.IsNew("MSOLAP", "PASCAL");

            registry.Reset();

            Assert.True(registry.IsNew("MSOLAP", "PASCAL"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_client_that_sends_no_user_agent_is_named_as_such(string userAgent)
        {
            // "no user-agent" is itself a clue about what connected, so it must not come
            // out as an empty gap in the line.
            Assert.Equal("(no user-agent)", XmlaClientRegistry.Describe(userAgent));
        }

        [Fact]
        public void A_long_user_agent_is_trimmed_so_one_client_cannot_flood_a_line()
        {
            string described = XmlaClientRegistry.Describe(new string('x', 400));

            Assert.Equal(121, described.Length);
            Assert.EndsWith("…", described);
        }

        [Fact]
        public void Describe_keeps_a_normal_user_agent_intact()
        {
            Assert.Equal("MSOLAP 16.0.5", XmlaClientRegistry.Describe("  MSOLAP 16.0.5  "));
        }
    }
}
