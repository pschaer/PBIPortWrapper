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
        /// <paramref name="log"/> is written to only when the Undo button had to be
        /// matched by its localized label because its AutomationId was not the expected
        /// one — i.e. when Desktop has changed it (#82).
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
                    string automationId;
                    try { automationId = button.Current.AutomationId; }
                    catch { automationId = null; }
                    if (UndoButtonMatcher.Matches(automationId, name))
                    {
                        LogUnexpectedAutomationId(button, name);
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
        /// Version-drift detector. The AutomationId is the primary match and has been
        /// <c>undo</c> on every Desktop seen so far, so a match on that id is the
        /// ordinary case and says nothing worth writing down. Only the exception is
        /// logged: a Desktop that matched via the localized label instead, which means
        /// the id moved and the primary match has quietly degraded to the fallback.
        /// </summary>
        private void LogUnexpectedAutomationId(AutomationElement button, string name)
        {
            if (_log == null) return;
            string id;
            try { id = button.Current.AutomationId ?? string.Empty; }
            catch { return; }
            if (UndoButtonMatcher.IsUndoAutomationId(id) || id == _loggedAutomationId) return;
            _loggedAutomationId = id;
            _log($"Undo button matched by label '{name}', not by AutomationId " +
                 $"(found '{id}'). Power BI Desktop may have changed it.");
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
