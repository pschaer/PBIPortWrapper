using System;
using System.Text;

namespace PBIRelay.Services
{
    /// <summary>
    /// Reads an HTTP Basic <c>Authorization</c> header (#132).
    ///
    /// This used to be HttpListener's job; moving the endpoint to Kestrel made it ours.
    /// It is deliberately only a decoder: it says what the caller CLAIMS, and answering
    /// whether that claim is true stays with Windows, in
    /// <see cref="WindowsCredentialValidator"/>. Nothing here stores, caches or compares
    /// a password, so this is not a credential store and must never become one.
    ///
    /// Base64 is encoding, not encryption — the whole reason #132 exists.
    /// </summary>
    public static class BasicCredentials
    {
        private const string Scheme = "Basic ";

        /// <summary>
        /// True when the header carries a usable user name and password. False for
        /// anything malformed, which the caller treats exactly like no credentials at
        /// all: an unreadable claim is not a weaker claim, it is no claim.
        /// </summary>
        public static bool TryParse(string authorizationHeader, out string user, out string password)
        {
            user = null;
            password = null;

            if (string.IsNullOrWhiteSpace(authorizationHeader)) return false;

            string header = authorizationHeader.Trim();
            if (!header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

            string encoded = header.Substring(Scheme.Length).Trim();
            if (encoded.Length == 0) return false;

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return false;
            }

            // Split on the FIRST colon only: a password may contain colons, a user name
            // may not. Splitting on all of them would silently truncate the password and
            // reject a caller whose credentials were correct.
            int separator = decoded.IndexOf(':');
            if (separator <= 0) return false;

            user = decoded.Substring(0, separator);
            password = decoded.Substring(separator + 1);
            return true;
        }
    }
}
