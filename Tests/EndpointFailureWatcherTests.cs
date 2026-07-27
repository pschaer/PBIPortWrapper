using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// The rule for interrupting someone about a dead endpoint. A failure that only
    /// reaches log.txt and a menu label is a failure nobody learns about until a client
    /// cannot connect; one announced on every status change is one nobody reads.
    /// </summary>
    public class EndpointFailureWatcherTests
    {
        private static EndpointStatus Failed(string reason) =>
            new EndpointStatus(enabled: true, running: false, port: 55555,
                authMode: BridgeAuthMode.Basic, error: reason);

        private static EndpointStatus Running() =>
            new EndpointStatus(enabled: true, running: true, port: 55555,
                authMode: BridgeAuthMode.Basic, boundPrefix: "https://+:55555/");

        [Fact]
        public void A_failure_is_announced()
        {
            var watcher = new EndpointFailureWatcher();

            Assert.True(watcher.ShouldAnnounce(Failed("No certificate file at 'x.pem'.")));
        }

        [Fact]
        public void The_same_failure_is_not_announced_twice()
        {
            // Status arrives on every serve, rule edit and policy change. Repeating an
            // unchanged failure on each would train the user to dismiss it.
            var watcher = new EndpointFailureWatcher();
            watcher.ShouldAnnounce(Failed("Port 55555 is in use."));

            Assert.False(watcher.ShouldAnnounce(Failed("Port 55555 is in use.")));
            Assert.False(watcher.ShouldAnnounce(Failed("Port 55555 is in use.")));
        }

        [Fact]
        public void A_different_failure_is_announced_again()
        {
            // Fixing the certificate and hitting a port clash is new information.
            var watcher = new EndpointFailureWatcher();
            watcher.ShouldAnnounce(Failed("No certificate file at 'x.pem'."));

            Assert.True(watcher.ShouldAnnounce(Failed("Port 55555 is in use.")));
        }

        [Fact]
        public void A_failure_after_a_recovery_is_announced_again()
        {
            // Even with the same reason: it failed, it worked, it failed again - the
            // second one is news, not the echo of the first.
            var watcher = new EndpointFailureWatcher();
            watcher.ShouldAnnounce(Failed("Port 55555 is in use."));
            watcher.ShouldAnnounce(Running());

            Assert.True(watcher.ShouldAnnounce(Failed("Port 55555 is in use.")));
        }

        [Fact]
        public void A_running_endpoint_says_nothing()
        {
            Assert.False(new EndpointFailureWatcher().ShouldAnnounce(Running()));
        }

        [Fact]
        public void An_endpoint_switched_off_on_purpose_is_not_a_failure()
        {
            var off = EndpointStatus.Off(55555, BridgeAuthMode.Basic);

            Assert.False(new EndpointFailureWatcher().ShouldAnnounce(off));
        }

        [Fact]
        public void Enabled_but_not_running_is_a_failure_even_without_a_message()
        {
            // Whatever the bookkeeping says, an endpoint that was asked to run and is
            // not running has failed, and the user who turned it on is owed that.
            var status = new EndpointStatus(enabled: true, running: false, port: 55555,
                authMode: BridgeAuthMode.Basic);

            Assert.True(new EndpointFailureWatcher().ShouldAnnounce(status));
        }

        [Fact]
        public void A_null_status_is_not_a_failure()
        {
            // The startup check reads whatever status exists, which may be none yet.
            Assert.False(new EndpointFailureWatcher().ShouldAnnounce(null));
        }
    }
}
