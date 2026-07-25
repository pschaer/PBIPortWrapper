using System;
using System.Windows.Forms;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// Wires the top-panel settings checkboxes (Minimize to Tray, Start with Windows)
    /// to config - initial value in, changes persisted out - and reconciles the Windows
    /// auto-start registry key with config at launch (#87). Keeps MainForm lean.
    /// </summary>
    public static class SettingsBinder
    {
        public static void Bind(CheckBox minimizeToTray, CheckBox startWithWindows, ConfigService config)
        {
            BindCheckbox(minimizeToTray,
                () => config.Current?.MinimizeToTray ?? false,
                v => config.SetMinimizeToTray(v));
            BindCheckbox(startWithWindows,
                () => config.Current?.StartWithWindows ?? false,
                v => config.SetStartWithWindows(v));

            // Self-heal the Run key if the exe moved or was deleted externally (#87).
            config.ReconcileStartup();
        }

        private static void BindCheckbox(CheckBox cb, Func<bool> getter, Action<bool> setter)
        {
            cb.Checked = getter();
            cb.CheckedChanged += (s, e) => setter(cb.Checked);
        }
    }
}
