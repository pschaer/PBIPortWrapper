using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace PBIPortWrapper.Presenters
{
    public class ViewContextMenuHandler
    {
        private readonly DataGridView _dataGridView;

        public ViewContextMenuHandler(DataGridView dataGridView)
        {
            _dataGridView = dataGridView;
        }

        public void OnOpenFolderClick(object sender, EventArgs e)
        {
            if (_dataGridView.SelectedRows.Count > 0)
            {
                var row = _dataGridView.SelectedRows[0];
                string toolTip = row.Cells["colModelName"].ToolTipText;
                string filePath = null;

                // The tooltip carries the AS workspace dir, labeled honestly (#59).
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Workspace: "))
                {
                    filePath = toolTip.Substring(toolTip.IndexOf("Workspace: ") + 11).Trim();
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                        }
                        else if (Directory.Exists(filePath))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", filePath);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(filePath);
                            if (Directory.Exists(dir))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", dir);
                            }
                            else
                            {
                                MessageBox.Show("Cannot open folder. Path does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Cannot open folder. The file path is not available (instance might be offline).", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        public void OnCopyPathClick(object sender, EventArgs e)
        {
            if (_dataGridView.SelectedRows.Count > 0)
            {
                var row = _dataGridView.SelectedRows[0];
                string toolTip = row.Cells["colModelName"].ToolTipText;
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Workspace: "))
                {
                    string filePath = toolTip.Substring(toolTip.IndexOf("Workspace: ") + 11).Trim();
                    Clipboard.SetText(filePath);
                }
            }
        }
    }
}
