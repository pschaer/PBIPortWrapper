using System;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class RepeatedEventLimiterTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 7, 27, 17, 3, 11, DateTimeKind.Utc);
        private static RepeatedEventLimiter OnePerMinute() => new RepeatedEventLimiter(TimeSpan.FromMinutes(1));

        [Fact]
        public void The_first_occurrence_is_always_reported()
        {
            Assert.True(OnePerMinute().ShouldReport("pascal@10.9.20.21", T0, out int suppressed));
            Assert.Equal(0, suppressed);
        }

        [Fact]
        public void A_client_retrying_within_the_window_says_it_once()
        {
            // The actual report: one wrong password produced three identical warnings in
            // the same second, because clients retry.
            var limiter = OnePerMinute();

            Assert.True(limiter.ShouldReport("pascal@10.9.20.21", T0, out _));
            Assert.False(limiter.ShouldReport("pascal@10.9.20.21", T0, out _));
            Assert.False(limiter.ShouldReport("pascal@10.9.20.21", T0, out _));
        }

        [Fact]
        public void After_the_window_it_speaks_again_and_says_how_many_it_swallowed()
        {
            // Sustained failures must stay visible - suppression that never ends would
            // hide someone working through a password list.
            var limiter = OnePerMinute();

            limiter.ShouldReport("pascal@10.9.20.21", T0, out _);
            limiter.ShouldReport("pascal@10.9.20.21", T0.AddSeconds(1), out _);
            limiter.ShouldReport("pascal@10.9.20.21", T0.AddSeconds(2), out _);

            Assert.True(limiter.ShouldReport("pascal@10.9.20.21", T0.AddMinutes(2), out int suppressed));
            Assert.Equal(2, suppressed);
        }

        [Fact]
        public void The_count_resets_after_it_has_been_reported()
        {
            var limiter = OnePerMinute();
            limiter.ShouldReport("k", T0, out _);
            limiter.ShouldReport("k", T0.AddSeconds(1), out _);
            limiter.ShouldReport("k", T0.AddMinutes(2), out _);   // reports "1 further"

            Assert.True(limiter.ShouldReport("k", T0.AddMinutes(4), out int suppressed));
            Assert.Equal(0, suppressed);
        }

        [Fact]
        public void Different_accounts_do_not_silence_each_other()
        {
            // Two people failing to sign in are two events, and the second must not be
            // hidden by the first.
            var limiter = OnePerMinute();

            Assert.True(limiter.ShouldReport("pascal@10.9.20.21", T0, out _));
            Assert.True(limiter.ShouldReport("someone-else@10.9.20.21", T0, out _));
            Assert.True(limiter.ShouldReport("pascal@10.9.30.31", T0, out _));
        }

        [Fact]
        public void A_restart_starts_loud()
        {
            var limiter = OnePerMinute();
            limiter.ShouldReport("k", T0, out _);

            limiter.Reset();

            Assert.True(limiter.ShouldReport("k", T0, out _));
        }

        [Fact]
        public void A_null_key_is_handled_rather_than_throwing_inside_a_request()
        {
            var limiter = OnePerMinute();
            Assert.True(limiter.ShouldReport(null, T0, out _));
            Assert.False(limiter.ShouldReport(null, T0, out _));
        }
    }
}
