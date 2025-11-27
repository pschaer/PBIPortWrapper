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

        public MainForm()
        {
            InitializeComponent();

            this.Text = "Power BI Port Wrapper v0.2";

            InitializeServices();
            InitializeEventHandlers();
            LoadConfiguration();

            LogMessage("Power BI Port Wrapper v0.2");
            LogMessage("Features: Multi-instance support, Auto-reconnect, Process detection");
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
            // 1. Mark all rows as "missing" initially (we'll unmark them if found)
            var rowsToRemove = new List<DataGridViewRow>();
            var activeProcessIds = instances.Select(i => i.ProcessId).ToHashSet();

            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                // Try to match by ProcessId (stored in Tag) first, then by Name
                int? rowProcessId = row.Tag as int?;
                string modelName = row.Cells["colModelName"].Value?.ToString();

                bool isFound = false;
                
                if (rowProcessId.HasValue && activeProcessIds.Contains(rowProcessId.Value))
                {
                    isFound = true;
                }
                else if (instances.Any(i => i.FileName == modelName))
                {
                    isFound = true;
                }

                if (!isFound)
                {
                    int fixedPort = 0;
                    if (row.Cells["colFixedPort"].Value != null)
                    {
                        int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
                    }

                    if (fixedPort > 0 && _proxyManager.IsRunning(fixedPort))
                    {
                        row.Cells["colStatus"].Value = "PBI Closed (Proxy Running)";
                        row.Cells["colStatus"].Style.ForeColor = Color.Orange;
                    }
                    else
                    {
                        rowsToRemove.Add(row);
                    }
                }
            }

            // Remove rows for closed instances (that aren't running proxies)
            foreach (var row in rowsToRemove)
            {
                dataGridViewInstances.Rows.Remove(row);
            }

            // 2. Add or Update rows
            foreach (var instance in instances)
            {
                // Try to find existing row by ProcessId (Tag)
                var existingRow = dataGridViewInstances.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(r => (r.Tag as int?) == instance.ProcessId);

                // Fallback: Find by Name if Tag is missing or mismatch (e.g. app restart)
                if (existingRow == null)
                {
                    existingRow = dataGridViewInstances.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => r.Cells["colModelName"].Value?.ToString() == instance.FileName);
                }

                if (existingRow == null)
                {
                    // New instance found
                    int rowIndex = dataGridViewInstances.Rows.Add();
                    existingRow = dataGridViewInstances.Rows[rowIndex];
                    existingRow.Tag = instance.ProcessId; // Store ProcessId
                    existingRow.Cells["colModelName"].Value = instance.FileName;
                    
                    // Apply saved rule or default
                    var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);
                    if (rule != null)
                    {
                        existingRow.Cells["colFixedPort"].Value = rule.FixedPort;
                        existingRow.Cells["colAuto"].Value = rule.AutoConnect;
                        existingRow.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    }
                    else
                    {
                        // Default: Suggest a port (e.g. 55555 + index)
                        int suggestedPort = 55555 + rowIndex;
                        existingRow.Cells["colFixedPort"].Value = suggestedPort;
                        existingRow.Cells["colAuto"].Value = false;
                        existingRow.Cells["colNetwork"].Value = false;
                    }
                }
                else
                {
                    // Update Tag and Name (in case it changed from Untitled -> Name)
                    existingRow.Tag = instance.ProcessId;
                    if (existingRow.Cells["colModelName"].Value?.ToString() != instance.FileName)
                    {
                        existingRow.Cells["colModelName"].Value = instance.FileName;
                        // Also try to re-apply rule if name changed
                        var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);
                        if (rule != null)
                        {
                            // Only update if not already set/running to avoid disrupting
                            if (!_proxyManager.IsRunning(Convert.ToInt32(existingRow.Cells["colFixedPort"].Value)))
                            {
                                existingRow.Cells["colFixedPort"].Value = rule.FixedPort;
                                existingRow.Cells["colAuto"].Value = rule.AutoConnect;
                                existingRow.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                            }
                        }
                    }
                }

                // Update PBI Port (it changes every restart)
                existingRow.Cells["colPbiPort"].Value = instance.Port;

                // Update Status if not running
                int fixedPort = Convert.ToInt32(existingRow.Cells["colFixedPort"].Value);
                if (_proxyManager.IsRunning(fixedPort))
                {
                    existingRow.Cells["colStatus"].Value = "Running";
                    existingRow.Cells["colStatus"].Style.ForeColor = Color.Green;
                    existingRow.Cells["colAction"].Value = "Stop";
                    existingRow.Cells["colFixedPort"].ReadOnly = true;
                    existingRow.Cells["colNetwork"].ReadOnly = true;
                }
                else
                {
                    existingRow.Cells["colStatus"].Value = "Ready";
                    existingRow.Cells["colStatus"].Style.ForeColor = Color.Black;
                    existingRow.Cells["colAction"].Value = "Start";
                    existingRow.Cells["colFixedPort"].ReadOnly = false;
                    existingRow.Cells["colNetwork"].ReadOnly = false;
                }
            }
        }

        private void ProcessAutoConnect(List<PowerBIInstance> instances)
        {
            foreach (DataGridViewRow row in dataGridViewInstances.Rows)
            {
                bool isAuto = Convert.ToBoolean(row.Cells["colAuto"].Value);
                string status = row.Cells["colStatus"].Value?.ToString();
                string modelName = row.Cells["colModelName"].Value?.ToString();
                
                if (isAuto && status == "Ready")
                {
                    var instance = instances.FirstOrDefault(i => i.FileName == modelName);
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

                // Check if port is valid
                if (fixedPort < 1024 || fixedPort > 65535)
                {
                    LogMessage($"Invalid port {fixedPort} for {instance.FileName}");
                    return;
                }

                bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);

                if (allowNetwork)
                {
                    // Ensure firewall rule exists (simple check/add)
                    // In a real app, we might want to check specifically, but re-running the command is usually safe or we can prompt.
                    // For auto-start, we assume user has configured it.
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
                if (Convert.ToInt32(row.Cells["colFixedPort"].Value) == fixedPort)
                {
                    row.Cells["colStatus"].Value = isRunning ? "Running" : "Ready";
                    row.Cells["colStatus"].Style.ForeColor = isRunning ? Color.Green : Color.Black;
                    row.Cells["colAction"].Value = isRunning ? "Stop" : "Start";
                    
                    // Lock editing while running
                    row.Cells["colFixedPort"].ReadOnly = isRunning;
                    row.Cells["colNetwork"].ReadOnly = isRunning;
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
                int fixedPort = Convert.ToInt32(row.Cells["colFixedPort"].Value);
                string modelName = row.Cells["colModelName"].Value?.ToString();

                if (action == "Start")
                {
                    var instance = _currentInstances.FirstOrDefault(i => i.FileName == modelName);
                    // Fallback to finding by ProcessId if name mismatch
                    if (instance == null && row.Tag is int pid)
                    {
                        instance = _currentInstances.FirstOrDefault(i => i.ProcessId == pid);
                    }

                    if (instance != null)
                    {
                        // Check for duplicate ports
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
                        
                        // Save rule on manual start
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
            if (string.IsNullOrEmpty(modelName)) return;

            int fixedPort = 0;
            if (row.Cells["colFixedPort"].Value != null)
            {
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
            }

            bool auto = Convert.ToBoolean(row.Cells["colAuto"].Value);
            bool network = Convert.ToBoolean(row.Cells["colNetwork"].Value);

            // Update or Add rule
            var rule = _config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);
            if (rule != null)
            {
                rule.FixedPort = fixedPort;
                rule.AutoConnect = auto;
                rule.AllowNetworkAccess = network;
            }
            else
            {
                _config.PortMappings.Add(new PortMappingRule(modelName, fixedPort, auto, network));
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