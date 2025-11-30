using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
                
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
                {
                    filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
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
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
                {
                    string filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
                    Clipboard.SetText(filePath);
                }
            }
        }

        public void OnCopyConnectionStringClick(object sender, EventArgs e)
        {
            if (_dataGridView.SelectedRows.Count > 0)
            {
                var row = _dataGridView.SelectedRows[0];
                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

                if (fixedPort > 0)
                {
                    bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);
                    string connectionString = $"localhost:{fixedPort}";

                    if (allowNetwork)
                    {
                        try
                        {
                            string hostName = System.Net.Dns.GetHostName();
                            var ipEntry = System.Net.Dns.GetHostEntry(hostName);
                            var ip = ipEntry.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                            if (ip != null)
                            {
                                connectionString = $"{ip}:{fixedPort}";
                            }
                        }
                        catch { }
                    }

                    Clipboard.SetText(connectionString);
                }
            }
        }
    }
}
