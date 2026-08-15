using System;
using Microsoft.Win32;

namespace PBIRelay.Services
{
    /// <summary>
    /// Manages the Windows auto-start registry entry (#87). Reads and writes
    /// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run so PBIRelay
    /// launches at login — no admin rights required.
    /// </summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PBIRelay";

        /// <summary>
        /// Returns true if the auto-start registry key is present.
        /// </summary>
        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }

        /// <summary>
        /// Creates (or updates) the Run key so PBIRelay starts at login.
        /// The entry points at the current exe with <c>--silent</c> so the
        /// app starts hidden in the tray.
        /// </summary>
        public static void Register()
        {
            string exePath = Environment.ProcessPath
                             ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            // Best-effort: a locked-down (group-policy) Run key must not crash a toggle.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.SetValue(ValueName, $"\"{exePath}\" --silent");
            }
            catch { /* leave unregistered; ReconcileStartup retries next launch */ }
        }

        /// <summary>
        /// Removes the Run key. No-op if the key does not exist.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch { /* best-effort */ }
        }
    }
}
