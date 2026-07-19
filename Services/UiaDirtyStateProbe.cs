using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Windows.Automation;
using PBIPortWrapper.Services;

namespace PBIPortWrapper
{
    /// <summary>
    /// The best available unsaved-changes probe for Desktop 2.155 (#57 findings):
    /// no title marker exists at the Win32 or UIA level, so the only signal is the
    /// Quick-Access-Toolbar Undo button. Undo disabled means no edits since open
    /// (Clean); Undo enabled means there were edits, which may or may not have been
    /// saved since (MaybeDirty — the undo stack survives a save). Anything that
    /// prevents reading the button (window not found, localized/renamed button,
    /// UIA failure) answers Unknown so the UI falls back to asking the user.
    /// </summary>
    public class UiaDirtyStateProbe : IDirtyStateProbe
    {
        public DirtyState Probe(int processId)
        {
            try
            {
                var hwnd = ResolveDesktopMainWindow(processId);
                if (hwnd == IntPtr.Zero) return DirtyState.Unknown;

                var root = AutomationElement.FromHandle(hwnd);
                var buttons = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                // PBI button names carry zero-width characters, so a
                // PropertyCondition on Name never matches — iterate and compare
                // stripped names instead (E-series findings, #57).
                foreach (AutomationElement button in buttons)
                {
                    string name = StripInvisibleChars(button.Current.Name);
                    if (name.StartsWith("Undo", StringComparison.OrdinalIgnoreCase))
                        return button.Current.IsEnabled ? DirtyState.MaybeDirty : DirtyState.Clean;
                }

                return DirtyState.Unknown;
            }
            catch
            {
                return DirtyState.Unknown;
            }
        }

        /// <summary>
        /// The pid handed to the probe is the msmdsrv.exe engine process; the
        /// window with the Undo button belongs to its PBIDesktop.exe parent.
        /// </summary>
        private static IntPtr ResolveDesktopMainWindow(int processId)
        {
            if (processId <= 0) return IntPtr.Zero;

            var process = Process.GetProcessById(processId);
            if (!process.ProcessName.Equals("PBIDesktop", StringComparison.OrdinalIgnoreCase))
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
                var parentId = searcher.Get().Cast<ManagementObject>()
                    .Select(p => Convert.ToInt32(p["ParentProcessId"]))
                    .FirstOrDefault();
                if (parentId <= 0) return IntPtr.Zero;

                process = Process.GetProcessById(parentId);
                if (!process.ProcessName.Equals("PBIDesktop", StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;
            }

            return process.MainWindowHandle;
        }

        private static string StripInvisibleChars(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return new string(value.Where(c =>
                !char.IsControl(c)
                && (c < '\u200B' || c > '\u200F')   // zero-width + directional marks
                && c != '\uFEFF').ToArray());          // zero-width no-break / BOM
        }
    }
}
