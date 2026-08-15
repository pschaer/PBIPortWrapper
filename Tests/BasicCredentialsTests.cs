using System;
using System.Text;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class BasicCredentialsTests
    {
        private static string Header(string user, string password) =>
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

        [Fact]
        public void A_well_formed_header_yields_the_name_and_password()
        {
            Assert.True(BasicCredentials.TryParse(Header("PASCAL", "hunter2"), out string user, out string password));
            Assert.Equal("PASCAL", user);
            Assert.Equal("hunter2", password);
        }

        [Fact]
        public void A_password_containing_colons_survives_intact()
        {
            // Splitting on every colon would truncate the password and refuse a caller
            // whose credentials were correct - a rejection with no visible cause.
            Assert.True(BasicCredentials.TryParse(Header("PASCAL", "a:b:c"), out _, out string password));
            Assert.Equal("a:b:c", password);
        }

        [Fact]
        public void A_domain_qualified_name_is_passed_through_for_Windows_to_judge()
        {
            Assert.True(BasicCredentials.TryParse(Header(@"CASE\PASCAL", "pw"), out string user, out _));
            Assert.Equal(@"CASE\PASCAL", user);
        }

        [Fact]
        public void An_empty_password_still_parses_so_Windows_is_the_one_that_refuses_it()
        {
            // Whether an empty password is acceptable is an account policy question, and
            // answering it here would be this class deciding something it must not.
            Assert.True(BasicCredentials.TryParse(Header("PASCAL", ""), out string user, out string password));
            Assert.Equal("PASCAL", user);
            Assert.Equal("", password);
        }

        [Fact]
        public void The_scheme_is_matched_case_insensitively_as_http_requires()
        {
            Assert.True(BasicCredentials.TryParse(
                "basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("u:p")), out _, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Bearer abc123")]                 // a different scheme entirely
        [InlineData("Basic")]                          // no payload
        [InlineData("Basic ")]                         // empty payload
        [InlineData("Basic !!!not base64!!!")]
        public void Anything_unusable_is_no_claim_at_all(string header)
        {
            // An unreadable claim is not a weaker claim: the caller treats every one of
            // these exactly like sending no credentials, and gets challenged.
            Assert.False(BasicCredentials.TryParse(header, out string user, out string password));
            Assert.Null(user);
            Assert.Null(password);
        }

        [Fact]
        public void A_payload_with_no_separator_is_refused_rather_than_read_as_a_bare_name()
        {
            string header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("PASCAL"));
            Assert.False(BasicCredentials.TryParse(header, out _, out _));
        }

        [Fact]
        public void An_empty_user_name_is_refused()
        {
            // ":password" names nobody. Passing it on would ask Windows to validate an
            // account with no name, which is a question with no useful answer.
            string header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":password"));
            Assert.False(BasicCredentials.TryParse(header, out _, out _));
        }

        [Fact]
        public void Non_ascii_credentials_are_decoded_as_utf8()
        {
            Assert.True(BasicCredentials.TryParse(Header("Schär", "pässwörd"), out string user, out string password));
            Assert.Equal("Schär", user);
            Assert.Equal("pässwörd", password);
        }
    }
}
