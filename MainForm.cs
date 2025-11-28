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

            this.Text = "PBI Port Wrapper v0.2";

            // Add Active Connections Column
            if (!dataGridViewInstances.Columns.Contains("colActive"))
            {
                var colActive = new DataGridViewTextBoxColumn();
                colActive.Name = "colActive";
                colActive.HeaderText = "Active";
                colActive.ReadOnly = true;
                colActive.Width = 60;
                dataGridViewInstances.Columns.Add(colActive);
            }

            // Configure Column Ordering and Alignment
            // Order: Model Name | PBI Port | Fixed Port | Auto | Network | Action | Status | Active
            dataGridViewInstances.Columns["colModelName"].DisplayIndex = 0;
            dataGridViewInstances.Columns["colPbiPort"].DisplayIndex = 1;
            dataGridViewInstances.Columns["colFixedPort"].DisplayIndex = 2;
            dataGridViewInstances.Columns["colAuto"].DisplayIndex = 3;
            dataGridViewInstances.Columns["colNetwork"].DisplayIndex = 4;
            dataGridViewInstances.Columns["colAction"].DisplayIndex = 5;
            dataGridViewInstances.Columns["colStatus"].DisplayIndex = 6;            
            dataGridViewInstances.Columns["colActive"].DisplayIndex = 7;

            // Center Content & Header: PBI Port, Fixed Port, Status, Active
            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colStatus", "colActive" })
            {
                dataGridViewInstances.Columns[colName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            // Center Header Only: Auto, Network, Action
            foreach (var colName in new[] { "colAuto", "colNetwork", "colAction" })
            {
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            InitializeServices();
            InitializeEventHandlers();
            InitializeContextMenu();
            LoadConfiguration();
            
            // Hide Refresh button as it's redundant
            buttonRefresh.Visible = false;

            LogMessage("PBI Port Wrapper v0.2");
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

            _proxyManager.OnProxyConnectionCountChanged += (sender, args) =>
            {
                UpdateActiveConnections(args.FixedPort, args.Count);
            };
        }

        private void InitializeEventHandlers()
        {
            // buttonRefresh.Click += (s, e) => RefreshInstances(); // Removed
            buttonOpenLogs.Click += ButtonOpenLogs_Click;
            
            dataGridViewInstances.CellContentClick += DataGridViewInstances_CellContentClick;
            dataGridViewInstances.CellValueChanged += DataGridViewInstances_CellValueChanged;
            dataGridViewInstances.CellEndEdit += DataGridViewInstances_CellEndEdit;
            dataGridViewInstances.CellValidating += DataGridViewInstances_CellValidating;
            dataGridViewInstances.CellEnter += DataGridViewInstances_CellEnter;
            
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

                // Priority 2: Match by Name (First come, first served)
                if (row == null)
                {
                    row = dataGridViewInstances.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => 
                        {
                            // Match if name is same AND it's not already running (Tag is null or different PID)
                            // But if Tag is set to another PID, it's another running instance, so skip.
                            // If Tag is null, it's an Offline row we can reuse.
                            return r.Cells["colModelName"].Value?.ToString() == instance.FileName && r.Tag == null;
                        });
                }

                if (row == null)
                {
                    // New Row
                    int rowIndex = dataGridViewInstances.Rows.Add();
                    row = dataGridViewInstances.Rows[rowIndex];
                    
                    // Apply saved rule if exists
                    var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);

                    // CHECK: Is this rule already active on another row?
                    bool isRuleActive = false;
                    if (rule != null)
                    {
                        foreach (DataGridViewRow r in dataGridViewInstances.Rows)
                        {
                            if (r == row) continue; // Should not happen as row is new, but for safety
                            
                            string rName = r.Cells["colModelName"].Value?.ToString();
                            if (rName == instance.FileName)
                            {
                                // Check if it has a port assigned (meaning it's using the config)
                                if (r.Cells["colFixedPort"].Value != null && 
                                    !string.IsNullOrEmpty(r.Cells["colFixedPort"].Value.ToString()))
                                {
                                    isRuleActive = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (rule != null && !isRuleActive)
                    {
                        row.Cells["colFixedPort"].Value = rule.FixedPort;
                        row.Cells["colAuto"].Value = rule.AutoConnect;
                        row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    }
                    else
                    {
                        // Default: Empty port for new instances (or if rule is already in use)
                        row.Cells["colFixedPort"].Value = null; 
                        row.Cells["colAuto"].Value = false;
                        row.Cells["colNetwork"].Value = false;
                        
                        if (isRuleActive)
                        {
                            LogMessage($"Duplicate instance detected for '{instance.FileName}'. Configuration not applied to avoid conflict.");
                        }
                    }
                }

                // MERGE LOGIC: Check if this running row (which might have just been renamed from "Untitled")
                // matches an existing "Offline" row. If so, merge them to prevent duplicates.
                if (row != null)
                {
                    var offlineRow = dataGridViewInstances.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => 
                            r != row && // Not the current row
                            r.Tag == null && // Is Offline
                            r.Cells["colModelName"].Value?.ToString() == instance.FileName); // Name matches

                    if (offlineRow != null)
                    {
                        // Found a duplicate offline row. Merge config into the running instance.
                        // We only merge if the running instance doesn't have a fixed port set yet, OR if we want to prioritize the saved config.
                        // Let's prioritize the saved config from the offline row.
                        
                        if (offlineRow.Cells["colFixedPort"].Value != null)
                            row.Cells["colFixedPort"].Value = offlineRow.Cells["colFixedPort"].Value;
                            
                        row.Cells["colAuto"].Value = offlineRow.Cells["colAuto"].Value;
                        row.Cells["colNetwork"].Value = offlineRow.Cells["colNetwork"].Value;
                        
                        // Remove the offline row
                        dataGridViewInstances.Rows.Remove(offlineRow);
                        LogMessage($"Merged offline config for '{instance.FileName}' into running instance.");
                    }
                }

                // Update Row Data
                row.Tag = instance.ProcessId;
                row.Cells["colModelName"].Value = instance.FileName;
                row.Cells["colModelName"].ToolTipText = $"Name: {instance.FileName}\nPath: {instance.FilePath}";
                row.Cells["colPbiPort"].Value = instance.Port;

                // Update Status
                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null && int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp))
                {
                    fixedPort = fp;
                }

                if (fixedPort > 0 && _proxyManager.IsRunning(fixedPort))
                {
                    SetRowStatus(row, "Running", Color.Green, "Stop", true);
                    // Update active count if possible (or wait for event)
                    int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                    row.Cells["colActive"].Value = activeCount;
                }
                else
                {
                    // If port is set, it's Ready to start. If not, it's waiting for input.
                    if (fixedPort > 0)
                        SetRowStatus(row, "Ready", Color.Black, "Start", false);
                    else
                        SetRowStatus(row, "Ready", Color.Black, "Set Port", false); // Placeholder text, button disabled
                    
                    row.Cells["colActive"].Value = "";
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
                
                var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);

                if (rule != null)
                {
                    // It's a saved config, keep it as "Offline"
                    row.Tag = null; // Clear ProcessId
                    row.Cells["colPbiPort"].Value = ""; // No PBI port
                    // Tooltip: We don't persist path, so just show name or generic info
                    row.Cells["colModelName"].ToolTipText = $"Name: {modelName}\n(Offline)";
                    
                    SetRowStatus(row, "Offline", Color.Gray, "Remove", false); 
                    row.Cells["colActive"].Value = "";
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
                    if (rule.ModelNamePattern == rowName)
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
                    row.Cells["colModelName"].ToolTipText = $"Name: {rule.ModelNamePattern}\n(Offline)";
                    row.Cells["colFixedPort"].Value = rule.FixedPort;
                    row.Cells["colAuto"].Value = rule.AutoConnect;
                    row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    row.Cells["colPbiPort"].Value = "";
                    SetRowStatus(row, "Offline", Color.Gray, "Remove", false);
                    row.Cells["colActive"].Value = "";
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

            // Disable button if action is "Set Port"
            if (actionText == "Set Port")
            {
                // We can't easily disable a button cell, but we can handle it in the click event
                // and maybe style it gray? The standard DataGridViewButtonCell doesn't support disabling easily.
                // We'll handle the logic in CellContentClick.
            }
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
                        int fixedPort = 0;
                        if (row.Cells["colFixedPort"].Value != null)
                            int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

                        if (fixedPort > 0)
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
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp) && fp == fixedPort)
                {
                    if (isRunning)
                    {
                        SetRowStatus(row, "Running", Color.Green, "Stop", true);
                        // Initial active count
                        int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                        row.Cells["colActive"].Value = activeCount;
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
                            SetRowStatus(row, "Offline", Color.Gray, "Remove", false);
                        }
                        row.Cells["colActive"].Value = "";
                    }
                }
            }
        }

        private void UpdateActiveConnections(int fixedPort, int count)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateActiveConnections(fixedPort, count)));
                return;
            }

            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                if (row.Cells["colFixedPort"].Value != null && 
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp) && fp == fixedPort)
                {
                    row.Cells["colActive"].Value = count;
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
                
                if (string.IsNullOrEmpty(action) || action == "Set Port") return; 

                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

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
                    // Check for active connections
                    int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                    if (activeCount > 0)
                    {
                        var result = MessageBox.Show(
                            $"There are {activeCount} active connection(s) to this proxy.\nStopping it will disconnect them.\n\nAre you sure you want to stop?", 
                            "Active Connections Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        
                        if (result != DialogResult.Yes) return;
                    }

                    _proxyManager.StopProxy(fixedPort);
                }
                else if (action == "Remove")
                {
                    // Remove configuration
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
                
                // Try to get path from ToolTip first (works for running instances)
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
                            // Try opening the directory of the file
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

        private void DeleteConfiguration(DataGridViewRow row)
        {
            string status = row.Cells["colStatus"].Value?.ToString();

            if (status == "Running")
            {
                MessageBox.Show("Cannot remove configuration while proxy is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string modelName = row.Cells["colModelName"].Value?.ToString();
            
            var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);

            if (rule != null)
            {
                var result = MessageBox.Show($"Remove configuration for '{modelName}'?", "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
                if (string.IsNullOrEmpty(e.FormattedValue.ToString())) return; // Allow empty

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

        private void DataGridViewInstances_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (dataGridViewInstances.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var row = dataGridViewInstances.Rows[e.RowIndex];
                if (row.Cells["colFixedPort"].Value == null || string.IsNullOrEmpty(row.Cells["colFixedPort"].Value.ToString()))
                {
                    // Suggest next available port
                    int suggestedPort = 55555;
                    while (IsPortDuplicate(suggestedPort, e.RowIndex))
                    {
                        suggestedPort++;
                    }
                    row.Cells["colFixedPort"].Value = suggestedPort;
                    
                    // Update UI immediately to show "Start" button
                    SetRowStatus(row, "Ready", Color.Black, "Start", false);
                }
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
            
            if (string.IsNullOrEmpty(modelName)) return;
            
            // Prevent saving "Untitled" to config
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            int fixedPort = 0;
            if (row.Cells["colFixedPort"].Value != null)
            {
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
            }

            // If port is 0 or empty, do not save/add rule yet? 
            // Actually, we might want to save preferences even if port is not set?
            // But usually we save when we have a port.
            
            if (fixedPort == 0) return;

            bool autoConnect = Convert.ToBoolean(row.Cells["colAuto"].Value);
            bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);

            var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);
            if (rule == null)
            {
                rule = new PortMappingRule
                {
                    ModelNamePattern = modelName,
                    FixedPort = fixedPort,
                    AutoConnect = autoConnect,
                    AllowNetworkAccess = allowNetwork
                };
                _config.PortMappings.Add(rule);
            }
            else
            {
                rule.FixedPort = fixedPort;
                rule.AutoConnect = autoConnect;
                rule.AllowNetworkAccess = allowNetwork;
            }

            SaveConfiguration();
        }

        private void ButtonOpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                string logFile = _configManager.GetLogFilePath();
                if (File.Exists(logFile))
                {
                    System.Diagnostics.Process.Start("notepad.exe", logFile);
                }
                else
                {
                    MessageBox.Show("Log file does not exist yet.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop all proxies
            if (_proxyManager.GetRunningPorts().Any())
            {
                var result = MessageBox.Show("Proxies are currently running. Are you sure you want to exit?", "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

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

            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
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