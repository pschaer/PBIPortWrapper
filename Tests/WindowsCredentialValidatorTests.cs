using System;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// The endpoint's Basic mode is only as good as this check, because HttpListener
    /// performs none: it decodes the header and admits the request whatever the
    /// password. These cover the refusals — the positive case needs a real password
    /// and is exercised by hand against a live account.
    /// </summary>
    public class WindowsCredentialValidatorTests
    {
        [Fact]
        public void AnAccountThatDoesNotExist_IsRejected()
        {
            Assert.False(WindowsCredentialValidator.IsValid(
                "pbi-port-wrapper-no-such-account-" + Guid.NewGuid().ToString("N"), "any-password"));
        }

        [Fact]
        public void TheCurrentUserWithAWrongPassword_IsRejected()
        {
            // The case that mattered: a real account name was previously enough,
            // because nothing looked at the password at all.
            Assert.False(WindowsCredentialValidator.IsValid(
                Environment.UserName, "certainly-not-the-password-" + Guid.NewGuid().ToString("N")));
        }

        [Theory]
        [InlineData(null, "password")]
        [InlineData("", "password")]
        [InlineData("   ", "password")]
        [InlineData("user", null)]
        [InlineData("user", "")]
        [InlineData("\\user", "password")]   // empty domain part
        public void IncompleteCredentials_AreRejectedWithoutAskingWindows(string user, string password)
        {
            // A blank password never reaches LogonUser: Windows would usually refuse
            // it for a network logon, but that depends on a policy setting and this
            // must not depend on one.
            Assert.False(WindowsCredentialValidator.IsValid(user, password));
        }

        [Fact]
        public void ItNeverThrows_WhateverItIsGiven()
        {
            // A failure to check must be a failure to authenticate, never an exception
            // that escapes into the request loop.
            Assert.False(WindowsCredentialValidator.IsValid("a\\b\\c", "x"));
            Assert.False(WindowsCredentialValidator.IsValid(new string('u', 5000), new string('p', 5000)));
            Assert.False(WindowsCredentialValidator.IsValid("user@", "x"));
        }
    }
}
