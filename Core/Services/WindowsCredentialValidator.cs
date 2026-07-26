using System;
using System.Runtime.InteropServices;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Verifies a user name and password against a Windows account on this machine.
    ///
    /// This exists because <see cref="System.Net.HttpListener"/>'s Basic scheme does
    /// <b>not</b> check passwords. It decodes the header and reports the name the
    /// caller claimed — <c>context.User.Identity.Name</c> is whatever they typed — and
    /// accepts the request. Measured: with <c>AuthenticationSchemes.Basic</c>, a
    /// nonexistent user with a made-up password is admitted exactly like a real one.
    /// Only a request carrying no credentials at all is challenged. Anything relying on
    /// the listener to authenticate Basic callers is therefore wide open.
    ///
    /// Nothing is stored and nothing is compared here: Windows is asked, through a
    /// network logon — the same check a file share performs. So account lockout,
    /// expiry and disabled accounts all apply for free, and the "never store
    /// credentials" rule is intact. The password lives only as long as the call.
    /// </summary>
    public static class WindowsCredentialValidator
    {
        /// <summary>
        /// Network logon: intended for validating credentials without creating an
        /// interactive session, and needs no privilege to call.
        /// </summary>
        private const int Logon32LogonNetwork = 3;

        private const int Logon32ProviderDefault = 0;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string userName, string domain, string password,
            int logonType, int logonProvider, out IntPtr token);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// True when these credentials name a Windows account this machine accepts.
        /// Never throws: a failure to check is a failure to authenticate.
        /// </summary>
        public static bool IsValid(string userName, string password)
        {
            // A blank password is refused outright rather than handed to Windows.
            // Windows would normally reject it for a network logon anyway, but that
            // depends on a policy setting, and this must not depend on one.
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)) return false;

            SplitAccount(userName, out string domain, out string account);
            if (string.IsNullOrWhiteSpace(account)) return false;

            IntPtr token = IntPtr.Zero;
            try
            {
                return LogonUser(account, domain, password, Logon32LogonNetwork, Logon32ProviderDefault, out token);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        /// <summary>
        /// Splits the forms a user may type. A bare name is looked up on this machine
        /// (<c>"."</c>), which is the account the wrapper's own docs tell them to use.
        /// </summary>
        private static void SplitAccount(string userName, out string domain, out string account)
        {
            string trimmed = userName.Trim();

            int backslash = trimmed.IndexOf('\\');
            if (backslash > 0)
            {
                domain = trimmed.Substring(0, backslash);
                account = trimmed.Substring(backslash + 1);
                return;
            }

            int at = trimmed.IndexOf('@');
            if (at > 0)
            {
                // A UPN carries its own domain; LogonUser accepts it with a null domain.
                domain = null;
                account = trimmed;
                return;
            }

            domain = ".";
            account = trimmed;
        }
    }
}
