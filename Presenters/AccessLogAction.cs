using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace PBIRelay.Presenters
{
    /// <summary>
    /// Opens the access log, from wherever it is offered — the tray and the dashboard
    /// both do, and one implementation means they cannot behave differently.
    /// </summary>
    public static class AccessLogAction
    {
        /// <summary>
        /// Opens a COPY, never the live file.
        ///
        /// Excel holds an open workbook for as long as its window is open, and a held
        /// file cannot be appended to — so opening the real access log to read it would
        /// stop it recording the very requests you opened it to look at. A snapshot has
        /// the obvious downside of not updating, which is the correct trade: a log you
        /// can read and that keeps recording beats a live view that costs you the data.
        /// </summary>
        public static void Open(string accessLogPath)
        {
            try
            {
                if (string.IsNullOrEmpty(accessLogPath) || !File.Exists(accessLogPath))
                {
                    MessageBox.Show(
                        "No requests have been recorded yet, so there is no access log to open.",
                        "Access log", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string folder = Path.Combine(Path.GetTempPath(), "PBIRelay");
                Directory.CreateDirectory(folder);

                // Timestamped, so opening it twice does not fight the copy still open
                // in Excel from the first time.
                string snapshot = Path.Combine(
                    folder, $"access log {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv");
                File.Copy(accessLogPath, snapshot, overwrite: true);

                Process.Start(new ProcessStartInfo(snapshot) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the access log: {ex.Message}",
                    "Access log", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
