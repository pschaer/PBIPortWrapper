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
    /// prevents reading the button (window not found, unrecognised localized
    /// button, UIA failure) answers Unknown so the UI falls back to asking the user.
    /// The Undo button is matched language-independently via <see cref="UndoButtonMatcher"/> (#82).
    /// </summary>
    public class UiaDirtyStateProbe : IDirtyStateProbe
    {
        private readonly Action<string> _log;
        private string _loggedAutomationId;

        /// <summary>
        /// <paramref name="log"/> receives a one-time diagnostic naming the Undo
        /// button's AutomationId when it is matched, so a running Desktop reveals it
        /// for a future label-independent match (#82).
        /// </summary>
        public UiaDirtyStateProbe(Action<string> log = null)
        {
            _log = log;
        }

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
                    if (UndoButtonMatcher.IsUndo(name))
                    {
                        LogAutomationId(button, name);
                        return button.Current.IsEnabled ? DirtyState.MaybeDirty : DirtyState.Clean;
                    }
                }

                return DirtyState.Unknown;
            }
            catch
            {
                return DirtyState.Unknown;
            }
        }

        /// <summary>
        /// One-time diagnostic: record the matched Undo button's AutomationId so a
        /// real Desktop reveals it (a stable id would beat label matching, #82).
        /// </summary>
        private void LogAutomationId(AutomationElement button, string name)
        {
            if (_log == null) return;
            string id;
            try { id = button.Current.AutomationId ?? string.Empty; }
            catch { return; }
            if (string.IsNullOrEmpty(id) || id == _loggedAutomationId) return;
            _loggedAutomationId = id;
            _log($"Undo button matched: AutomationId='{id}', Name='{name}'.");
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
