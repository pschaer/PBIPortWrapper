using System;
using System.IO;
using System.Windows.Forms;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// The grid context menu's two workspace actions.
    ///
    /// The target instance is set by <see cref="ViewEventCoordinator"/> when the menu
    /// opens, resolved from where the pointer actually is. It used to be read from the
    /// grid's selection, which is what broke the menu inside an expanded details panel
    /// (#151): those panels are child controls of the grid, so a right-click in one
    /// never reaches the grid's mouse handling. The selection stayed on whatever row was
    /// last right-clicked in the grid itself - and a plain left-click clears it, since
    /// the grid selects cells, not rows. The menu then either did nothing at all or
    /// acted on a different model than the panel it was invoked from.
    /// </summary>
    public class ViewContextMenuHandler
    {
        private readonly Action<string> _log;
        private PowerBIInstance _target;

        public ViewContextMenuHandler(Action<string> logCallback)
        {
            _log = logCallback;
        }

        /// <summary>
        /// The instance the menu acts on, or null when the pointer was over a row whose
        /// model is not running - an offline row has no workspace to open.
        /// </summary>
        public void SetTarget(PowerBIInstance instance) => _target = instance;

        public void OnOpenFolderClick(object sender, EventArgs e)
        {
            string path = _target?.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                Unavailable("Open Workspace Folder");
                return;
            }

            try
            {
                // FilePath is the Analysis Services workspace directory, so that is the
                // expected case; the file branch stays for the paths that are not.
                if (Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
                else if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else
                {
                    string parent = Path.GetDirectoryName(path);
                    if (Directory.Exists(parent))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", parent);
                    }
                    else
                    {
                        _log?.Invoke($"Workspace folder for '{_target.FileName}' does not exist: {path}");
                        MessageBox.Show(
                            "Cannot open folder. The workspace folder no longer exists - " +
                            "Power BI Desktop removes it when the model is closed.",
                            "Open Workspace Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Opening the workspace folder for '{_target.FileName}' failed: {ex.Message}");
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnCopyPathClick(object sender, EventArgs e)
        {
            string path = _target?.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                Unavailable("Copy Workspace Path");
                return;
            }

            try
            {
                Clipboard.SetText(path);
            }
            catch (Exception ex)
            {
                // The clipboard can be locked by another process; saying nothing here is
                // how a copy that never happened looks exactly like one that did.
                _log?.Invoke($"Copying the workspace path failed: {ex.Message}");
                MessageBox.Show($"Error copying path: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Both items need a running instance. Every path out of here says why rather
        /// than returning quietly, which is how #151 stayed invisible in the log.
        /// </summary>
        private void Unavailable(string action)
        {
            _log?.Invoke($"{action}: the model is not running, so it has no workspace folder.");
            MessageBox.Show(
                "This model is not running. Its workspace folder exists only while " +
                "Power BI Desktop has the model open.",
                action, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
