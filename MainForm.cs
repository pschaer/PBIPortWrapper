using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper
{
    public partial class MainForm : Form
    {
        private PowerBIDetector _detector;
        private ProxyManager _proxyManager;
        private ConfigurationManager _configManager;
        private ProxyConfiguration _config;
        
        // Cache of current instances to track state
        private List<PowerBIInstance> _currentInstances = new List<PowerBIInstance>();
        private ContextMenuStrip _contextMenuGrid;

        public MainForm()
        {
            InitializeComponent();

            this.Text = "Power BI Port Wrapper v0.3";

            InitializeServices();
            InitializeEventHandlers();
            InitializeContextMenu();
            LoadConfiguration();

            LogMessage("Power BI Port Wrapper v0.3");
            LogMessage("Features: Multi-instance support, Auto-reconnect, Offline config management");
            LogMessage($"Log file: {_configManager.GetLogFilePath()}");
            LogMessage("");

            // Initial refresh
            RefreshInstances();
        }

        private void InitializeServices()
        {
            _detector = new PowerBIDetector();
            _proxyManager = new ProxyManager();
            _configManager = new ConfigurationManager();

            _proxyManager.OnLog += (sender, message) => LogMessage(message);
            _proxyManager.OnError += (sender, message) => LogMessage($"ERROR: {message}");
            
            _proxyManager.OnProxyStarted += (sender, args) => 
            {
                UpdateGridStatus(args.FixedPort, true);
                LogMessage($"Started proxy on port {args.FixedPort} -> {args.TargetPort}");
            };

            _proxyManager.OnProxyStopped += (sender, args) => 
            {
                UpdateGridStatus(args.FixedPort, false);
                LogMessage($"Stopped proxy on port {args.FixedPort}");
            };
        }

        private void InitializeEventHandlers()
        {
            buttonRefresh.Click += (s, e) => RefreshInstances();
            buttonOpenLogs.Click += ButtonOpenLogs_Click;
            
            dataGridViewInstances.CellContentClick += DataGridViewInstances_CellContentClick;
            dataGridViewInstances.CellValueChanged += DataGridViewInstances_CellValueChanged;
            dataGridViewInstances.CellEndEdit += DataGridViewInstances_CellEndEdit;
            dataGridViewInstances.CellValidating += DataGridViewInstances_CellValidating;
            
            timerUpdate.Tick += (s, e) => RefreshInstances();
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeContextMenu()
        {
            _contextMenuGrid = new ContextMenuStrip();
            
            var openFolderItem = new ToolStripMenuItem("Open Folder");
            openFolderItem.Click += OpenFolder_Click;
            _contextMenuGrid.Items.Add(openFolderItem);

            var copyPathItem = new ToolStripMenuItem("Copy Path");
            copyPathItem.Click += CopyPath_Click;
            _contextMenuGrid.Items.Add(copyPathItem);

            _contextMenuGrid.Items.Add(new ToolStripSeparator());

            var deleteItem = new ToolStripMenuItem("Delete Configuration");
            deleteItem.Click += DeleteConfiguration_Click;
            _contextMenuGrid.Items.Add(deleteItem);
            
            dataGridViewInstances.ContextMenuStrip = _contextMenuGrid;
            dataGridViewInstances.MouseDown += DataGridViewInstances_MouseDown;
        }

        private void LoadConfiguration()
        {
            _config = _configManager.LoadConfiguration();
        }

        private void SaveConfiguration()
        {
            _configManager.SaveConfiguration(_config);
        }

        private void RefreshInstances()
        {
            if (!_detector.IsWorkspacePathValid())
            {
                return;
            }

            var detectedInstances = _detector.DetectRunningInstances();
            
            // Update internal list
            _currentInstances = detectedInstances;

            // Sync with Grid
            SyncGridWithInstances(detectedInstances);

            // Handle Auto-Connect
            ProcessAutoConnect(detectedInstances);
        }

        private void SyncGridWithInstances(List<PowerBIInstance> instances)
        {
            var activeProcessIds = instances.Select(i => i.ProcessId).ToHashSet();
            var processedRows = new HashSet<DataGridViewRow>();

            // 1. Update existing rows with detected instances
            foreach (var instance in instances)
            {
                DataGridViewRow row = null;

                // Priority 1: Match by ProcessId (Tag) - for currently running/tracked instances
                row = dataGridViewInstances.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(r => (r.Tag as int?) == instance.ProcessId);

                // Priority 2: Match by FilePath - for Offline rows that are coming back online
                if (row == null && !string.IsNullOrEmpty(instance.FilePath))
                {
                    row = dataGridViewInstances.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => 
                        {
                            // Check hidden path stored in Tag or ToolTip? 
                            // We store path in ToolTipText but parsing it is messy if we change format.
                            // Better to rely on the config match logic or store path in a hidden cell/Tag object.
                            // For now, let's check the ToolTipText if it contains the path.
                            string toolTip = r.Cells["colModelName"].ToolTipText;
                            return toolTip != null && toolTip.Contains(instance.FilePath);
                        });
                }

                // Priority 3: Match by FileName - Legacy fallback or if paths are identical/missing
                if (row == null)
                {
                    row = dataGridViewInstances.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => r.Cells["colModelName"].Value?.ToString() == instance.FileName);
                }

                if (row == null)
                {
                    // New Row
                    int rowIndex = dataGridViewInstances.Rows.Add();
                    row = dataGridViewInstances.Rows[rowIndex];
                    
                    // Apply saved rule if exists
                    var rule = _config.PortMappings.FirstOrDefault(r => 
                        (!string.IsNullOrEmpty(r.FilePath) && r.FilePath == instance.FilePath) || 
                        r.ModelNamePattern == instance.FileName);

                    if (rule != null)
                    {
                        row.Cells["colFixedPort"].Value = rule.FixedPort;
                        row.Cells["colAuto"].Value = rule.AutoConnect;
                        row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    }
                    else
                    {
                        // Default
                        int suggestedPort = 55555 + rowIndex;
                        row.Cells["colFixedPort"].Value = suggestedPort;
                        row.Cells["colAuto"].Value = false;
                        row.Cells["colNetwork"].Value = false;
                    }
                }

                // Update Row Data
                row.Tag = instance.ProcessId;
                row.Cells["colModelName"].Value = instance.FileName;
                row.Cells["colModelName"].ToolTipText = $"Name: {instance.FileName}\nPath: {instance.FilePath}";
                row.Cells["colPbiPort"].Value = instance.Port;

                // Update Status
                int fixedPort = Convert.ToInt32(row.Cells["colFixedPort"].Value);
                if (_proxyManager.IsRunning(fixedPort))
                {
                    SetRowStatus(row, "Running", Color.Green, "Stop", true);
                }
                else
                {
                    SetRowStatus(row, "Ready", Color.Black, "Start", false);
                }

                processedRows.Add(row);
            }

            // 2. Handle rows that are NOT in the detected list (Offline or Closed)
            var rowsToRemove = new List<DataGridViewRow>();
            
            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                if (processedRows.Contains(row)) continue;

                // This row represents an instance that is no longer detected.
                
                // Get current fixed port
                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                {
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
                }

                // Stop proxy if it was running (User Requirement: Stop on close)
                if (fixedPort > 0 && _proxyManager.IsRunning(fixedPort))
                {
                    _proxyManager.StopProxy(fixedPort);
                    LogMessage($"Auto-stopped proxy on port {fixedPort} (PBI closed)");
                }

                // Check if this row has a saved configuration
                string modelName = row.Cells["colModelName"].Value?.ToString();
                // Extract path from tooltip if possible, or rely on name match
                string toolTip = row.Cells["colModelName"].ToolTipText;
                string filePath = null;
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
                {
                    filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
                }

                var rule = _config.PortMappings.FirstOrDefault(r => 
                    (!string.IsNullOrEmpty(r.FilePath) && r.FilePath == filePath) || 
                    r.ModelNamePattern == modelName);

                if (rule != null)
                {
                    // It's a saved config, keep it as "Offline"
                    row.Tag = null; // Clear ProcessId
                    row.Cells["colPbiPort"].Value = ""; // No PBI port
                    SetRowStatus(row, "Offline", Color.Gray, "Delete", false); // Action is now Delete
                }
                else
                {
                    // No saved config, just a transient instance that closed -> Remove
                    rowsToRemove.Add(row);
                }
            }

            foreach (var row in rowsToRemove)
            {
                dataGridViewInstances.Rows.Remove(row);
            }
            
            // 3. Ensure all saved configs have a row (even if never detected yet)
            foreach (var rule in _config.PortMappings)
            {
                bool exists = false;
                foreach (DataGridViewRow row in dataGridViewInstances.Rows)
                {
                    string rowName = row.Cells["colModelName"].Value?.ToString();
                    string toolTip = row.Cells["colModelName"].ToolTipText;
                    
                    // Check path match first
                    if (!string.IsNullOrEmpty(rule.FilePath) && !string.IsNullOrEmpty(toolTip) && toolTip.Contains(rule.FilePath))
                    {
                        exists = true;
                        break;
                    }
                    // Fallback to name match
                    if (string.IsNullOrEmpty(rule.FilePath) && rule.ModelNamePattern == rowName)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    int rowIndex = dataGridViewInstances.Rows.Add();
                    var row = dataGridViewInstances.Rows[rowIndex];
                    row.Cells["colModelName"].Value = rule.ModelNamePattern;
                    row.Cells["colModelName"].ToolTipText = $"Name: {rule.ModelNamePattern}\nPath: {rule.FilePath}";
                    row.Cells["colFixedPort"].Value = rule.FixedPort;
                    row.Cells["colAuto"].Value = rule.AutoConnect;
                    row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    row.Cells["colPbiPort"].Value = "";
                    SetRowStatus(row, "Offline", Color.Gray, "Delete", false);
                }
            }
        }

        private void SetRowStatus(DataGridViewRow row, string status, Color color, string actionText, bool isReadOnly)
        {
            row.Cells["colStatus"].Value = status;
            row.Cells["colStatus"].Style.ForeColor = color;
            row.Cells["colAction"].Value = actionText;
            row.Cells["colFixedPort"].ReadOnly = isReadOnly;
            row.Cells["colNetwork"].ReadOnly = isReadOnly;
        }

        private void ProcessAutoConnect(List<PowerBIInstance> instances)
        {
            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                bool isAuto = Convert.ToBoolean(row.Cells["colAuto"].Value);
                string status = row.Cells["colStatus"].Value?.ToString();
                
                if (isAuto && status == "Ready")
                {
                    // Find instance
                    int? pid = row.Tag as int?;
                    var instance = instances.FirstOrDefault(i => i.ProcessId == pid);
                    
                    if (instance != null)
                    {
                        int fixedPort = Convert.ToInt32(row.Cells["colFixedPort"].Value);
                        StartProxySafe(instance, fixedPort, row);
                    }
                }
            }
        }

        private async void StartProxySafe(PowerBIInstance instance, int fixedPort, DataGridViewRow row)
        {
            try
            {
                if (_proxyManager.IsRunning(fixedPort)) return;

                if (fixedPort < 1024 || fixedPort > 65535)
                {
                    LogMessage($"Invalid port {fixedPort} for {instance.FileName}");
                    return;
                }

                bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);

                if (allowNetwork)
                {
                    // Firewall check logic here if needed
                }

                await _proxyManager.StartProxyAsync(fixedPort, instance.Port, allowNetwork);
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start {instance.FileName}: {ex.Message}");
            }
        }

        private void UpdateGridStatus(int fixedPort, bool isRunning)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateGridStatus(fixedPort, isRunning)));
                return;
            }

            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                if (row.Cells["colFixedPort"].Value != null && 
                    Convert.ToInt32(row.Cells["colFixedPort"].Value) == fixedPort)
                {
                    if (isRunning)
                    {
                        SetRowStatus(row, "Running", Color.Green, "Stop", true);
                    }
                    else
                    {
                        // Check if it's still a valid instance or just offline
                        string pbiPort = row.Cells["colPbiPort"].Value?.ToString();
                        if (!string.IsNullOrEmpty(pbiPort))
                        {
                            SetRowStatus(row, "Ready", Color.Black, "Start", false);
                        }
                        else
                        {
                            SetRowStatus(row, "Offline", Color.Gray, "Delete", false);
                        }
                    }
                }
            }
        }

        private async void DataGridViewInstances_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (dataGridViewInstances.Columns[e.ColumnIndex].Name == "colAction")
            {
                var row = dataGridViewInstances.Rows[e.RowIndex];
                string action = row.Cells["colAction"].Value?.ToString();
                
                if (string.IsNullOrEmpty(action)) return;

                int fixedPort = Convert.ToInt32(row.Cells["colFixedPort"].Value);

                if (action == "Start")
                {
                    int? pid = row.Tag as int?;
                    var instance = _currentInstances.FirstOrDefault(i => i.ProcessId == pid);

                    if (instance != null)
                    {
                        if (IsPortDuplicate(fixedPort, e.RowIndex))
                        {
                            MessageBox.Show($"Port {fixedPort} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);
                        if (allowNetwork)
                        {
                            var result = MessageBox.Show(
                                "Network Access is enabled for this instance.\nEnsure Windows Firewall allows inbound connections on port " + fixedPort + ".\n\nContinue?", 
                                "Network Access", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (result != DialogResult.Yes) return;
                        }

                        StartProxySafe(instance, fixedPort, row);
                        UpdateAndSaveRule(row);
                    }
                    else
                    {
                        MessageBox.Show("Power BI instance not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (action == "Stop")
                {
                    _proxyManager.StopProxy(fixedPort);
                }
                else if (action == "Delete")
                {
                    // Delete logic directly from button
                    DeleteConfiguration(row);
                }
            }
        }

        private void DataGridViewInstances_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = dataGridViewInstances.HitTest(e.X, e.Y);
                if (hit.RowIndex >= 0)
                {
                    dataGridViewInstances.ClearSelection();
                    dataGridViewInstances.Rows[hit.RowIndex].Selected = true;
                }
            }
        }

        private void OpenFolder_Click(object sender, EventArgs e)
        {
            if (dataGridViewInstances.SelectedRows.Count > 0)
            {
                var row = dataGridViewInstances.SelectedRows[0];
                string toolTip = row.Cells["colModelName"].ToolTipText;
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
                {
                    string filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
                    if (File.Exists(filePath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    }
                }
            }
        }

        private void CopyPath_Click(object sender, EventArgs e)
        {
            if (dataGridViewInstances.SelectedRows.Count > 0)
            {
                var row = dataGridViewInstances.SelectedRows[0];
                string toolTip = row.Cells["colModelName"].ToolTipText;
                if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
                {
                    string filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
                    Clipboard.SetText(filePath);
                }
            }
        }

        private void DeleteConfiguration_Click(object sender, EventArgs e)
        {
            if (dataGridViewInstances.SelectedRows.Count > 0)
            {
                DeleteConfiguration(dataGridViewInstances.SelectedRows[0]);
            }
        }

        private void DeleteConfiguration(DataGridViewRow row)
        {
            string status = row.Cells["colStatus"].Value?.ToString();

            if (status == "Running")
            {
                MessageBox.Show("Cannot delete configuration while proxy is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string modelName = row.Cells["colModelName"].Value?.ToString();
            string toolTip = row.Cells["colModelName"].ToolTipText;
            string filePath = null;
            if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
            {
                filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
            }

            var rule = _config.PortMappings.FirstOrDefault(r => 
                (!string.IsNullOrEmpty(r.FilePath) && r.FilePath == filePath) || 
                r.ModelNamePattern == modelName);

            if (rule != null)
            {
                var result = MessageBox.Show($"Delete configuration for '{modelName}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    _config.PortMappings.Remove(rule);
                    SaveConfiguration();
                    RefreshInstances(); // Will remove the row if it's offline
                }
            }
        }

        private bool IsPortDuplicate(int port, int currentRowIndex)
        {
            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                if (row.Index == currentRowIndex) continue;
                
                if (row.Cells["colFixedPort"].Value != null && 
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int otherPort))
                {
                    if (otherPort == port) return true;
                }
            }
            return false;
        }

        private void DataGridViewInstances_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (dataGridViewInstances.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                if (!int.TryParse(e.FormattedValue.ToString(), out int newPort))
                {
                    e.Cancel = true;
                    dataGridViewInstances.Rows[e.RowIndex].ErrorText = "Port must be a number";
                    return;
                }

                if (IsPortDuplicate(newPort, e.RowIndex))
                {
                    e.Cancel = true;
                    MessageBox.Show($"Port {newPort} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                dataGridViewInstances.Rows[e.RowIndex].ErrorText = string.Empty;
            }
        }

        private void DataGridViewInstances_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
             UpdateAndSaveRule(dataGridViewInstances.Rows[e.RowIndex]);
        }
        
        private void DataGridViewInstances_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                string colName = dataGridViewInstances.Columns[e.ColumnIndex].Name;
                if (colName == "colAuto" || colName == "colNetwork")
                {
                    UpdateAndSaveRule(dataGridViewInstances.Rows[e.RowIndex]);
                }
            }
        }

        private void UpdateAndSaveRule(DataGridViewRow row)
        {
            string modelName = row.Cells["colModelName"].Value?.ToString();
            string toolTip = row.Cells["colModelName"].ToolTipText;
            string filePath = null;
            if (!string.IsNullOrEmpty(toolTip) && toolTip.Contains("Path: "))
            {
                filePath = toolTip.Substring(toolTip.IndexOf("Path: ") + 6).Trim();
            }
            
            if (string.IsNullOrEmpty(modelName)) return;

            int fixedPort = 0;
            if (row.Cells["colFixedPort"].Value != null)
            {
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
            }

            bool auto = Convert.ToBoolean(row.Cells["colAuto"].Value);
            bool network = Convert.ToBoolean(row.Cells["colNetwork"].Value);

            // Update or Add rule
            var rule = _config.PortMappings.FirstOrDefault(r => 
                (!string.IsNullOrEmpty(r.FilePath) && r.FilePath == filePath) || 
                r.ModelNamePattern == modelName);

            if (rule != null)
            {
                rule.FixedPort = fixedPort;
                rule.AutoConnect = auto;
                rule.AllowNetworkAccess = network;
                rule.FilePath = filePath; // Ensure path is saved
            }
            else
            {
                _config.PortMappings.Add(new PortMappingRule(modelName, fixedPort, auto, network, filePath));
            }

            SaveConfiguration();
        }

        private void ButtonOpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                string logPath = _configManager.GetAppDataPath();
                System.Diagnostics.Process.Start("explorer.exe", logPath);
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening log folder: {ex.Message}");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _proxyManager.StopAll();
            SaveConfiguration();
        }

        private void LogMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => LogMessage(message)));
                return;
            }

            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            textBoxLog.AppendText($"{timestampedMessage}{Environment.NewLine}");
            
            try
            {
                string logFile = _configManager.GetLogFilePath();
                File.AppendAllText(logFile, timestampedMessage + Environment.NewLine);
            }
            catch { }
        }
    }
}