using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using PBIPortWrapper.Presenters;

namespace PBIPortWrapper
{
    public partial class MainForm : Form
    {
        // Services
        private PowerBIDetector _detector;
        private ProxyManager _proxyManager;
        private ConfigurationManager _configManager;
        private ValidationService _validationService;
        private ProxyConfiguration _config;
        
        // Presenters
        private GridPresenter _gridPresenter;
        private ProxyPresenter _proxyPresenter;
        private ConfigPresenter _configPresenter;
        
        // State
        private List<PowerBIInstance> _currentInstances = new List<PowerBIInstance>();

        public MainForm()
        {
            InitializeComponent();
            ConfigureGridColumns(); // Extracted from constructor
            
            // Set Icon
            try 
            { 
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.png");
                if (File.Exists(iconPath))
                {
                    using (var bmp = new Bitmap(iconPath))
                    {
                        var icon = Icon.FromHandle(bmp.GetHicon());
                        this.Icon = icon;
                        this.notifyIcon.Icon = icon;
                    }
                }
            } 
            catch { }

            InitializeServices();
            InitializePresenters();
            InitializeEventHandlers();
            InitializeContextMenu();
            
            // Load config
            _config = _configPresenter.LoadConfiguration();
            
            // Initial refresh
            RefreshInstances();
        }

        private void ConfigureGridColumns()
        {
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
            dataGridViewInstances.Columns["colModelName"].DisplayIndex = 0;
            dataGridViewInstances.Columns["colPbiPort"].DisplayIndex = 1;
            dataGridViewInstances.Columns["colFixedPort"].DisplayIndex = 2;
            dataGridViewInstances.Columns["colAuto"].DisplayIndex = 3;
            dataGridViewInstances.Columns["colNetwork"].DisplayIndex = 4;
            dataGridViewInstances.Columns["colAction"].DisplayIndex = 5;
            dataGridViewInstances.Columns["colStatus"].DisplayIndex = 6;            
            dataGridViewInstances.Columns["colActive"].DisplayIndex = 7;

            // Center Content & Header
            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colStatus", "colActive" })
            {
                dataGridViewInstances.Columns[colName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            // Center Header Only
            foreach (var colName in new[] { "colAuto", "colNetwork", "colAction" })
            {
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            
            // Hide Refresh button
            buttonRefresh.Visible = false;

            LogMessage("PBI Port Wrapper v0.2");
            LogMessage("Features: Multi-instance support, Auto-reconnect, Offline config management");
            LogMessage($"Log file: {new ConfigurationManager().GetLogFilePath()}"); // Temp instance just for path
            LogMessage("");
        }

        private void InitializeServices()
        {
            _detector = new PowerBIDetector();
            _proxyManager = new ProxyManager();
            _configManager = new ConfigurationManager();
            _validationService = new ValidationService();

            _proxyManager.OnLog += (sender, message) => LogMessage(message);
            _proxyManager.OnError += (sender, message) => LogMessage($"ERROR: {message}");
            
            // Wire up ProxyManager events to GridPresenter
            _proxyManager.OnProxyStarted += (sender, args) => 
            {
                _gridPresenter?.UpdateGridStatus(args.FixedPort, true);
                LogMessage($"Started proxy on port {args.FixedPort} -> {args.TargetPort}");
            };

            _proxyManager.OnProxyStopped += (sender, args) => 
            {
                _gridPresenter?.UpdateGridStatus(args.FixedPort, false);
                LogMessage($"Stopped proxy on port {args.FixedPort}");
            };

            _proxyManager.OnProxyConnectionCountChanged += (sender, args) =>
            {
                _gridPresenter?.UpdateActiveConnections(args.FixedPort, args.Count);
            };
        }

        private void InitializePresenters()
        {
            // We need _config loaded before creating GridPresenter? 
            // Actually GridPresenter takes _config.
            // So we should load config first or pass the manager?
            // The plan says ConfigPresenter loads it.
            // So let's create ConfigPresenter first.
            
            _configPresenter = new ConfigPresenter(_configManager, LogMessage);
            _config = _configPresenter.LoadConfiguration(); // Load it here so we can pass it to GridPresenter

            _proxyPresenter = new ProxyPresenter(_proxyManager, _validationService, LogMessage);
            
            _gridPresenter = new GridPresenter(
                dataGridViewInstances, 
                _proxyManager, 
                _validationService, 
                _config, 
                LogMessage);
        }

        private void InitializeEventHandlers()
        {
            buttonOpenLogs.Click += ButtonOpenLogs_Click;
            
            dataGridViewInstances.CellContentClick += DataGridViewInstances_CellContentClick;
            dataGridViewInstances.CellValueChanged += DataGridViewInstances_CellValueChanged;
            dataGridViewInstances.CellEndEdit += DataGridViewInstances_CellEndEdit;
            dataGridViewInstances.CellValidating += DataGridViewInstances_CellValidating;
            dataGridViewInstances.CellEnter += DataGridViewInstances_CellEnter;
            
            timerUpdate.Tick += (s, e) => RefreshInstances();
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
            
            checkBoxMinimizeToTray.CheckedChanged += (s, e) => 
            {
                if (_config != null)
                {
                    _config.MinimizeToTray = checkBoxMinimizeToTray.Checked;
                    _configPresenter.SaveConfiguration(_config);
                }
            };
            
            if (_config != null)
            {
                checkBoxMinimizeToTray.Checked = _config.MinimizeToTray;
            }
        }

        private void InitializeContextMenu()
        {
            var openFolderItem = new ToolStripMenuItem("Open Folder");
            openFolderItem.Click += OpenFolder_Click;
            contextMenuStripGrid.Items.Add(openFolderItem);

            var copyPathItem = new ToolStripMenuItem("Copy Path");
            copyPathItem.Click += CopyPath_Click;
            contextMenuStripGrid.Items.Add(copyPathItem);
            
            dataGridViewInstances.ContextMenuStrip = contextMenuStripGrid;
            dataGridViewInstances.MouseDown += DataGridViewInstances_MouseDown;
        }

        private void RefreshInstances()
        {
            if (!_detector.IsWorkspacePathValid()) return;

            var detectedInstances = _detector.DetectRunningInstances();
            _currentInstances = detectedInstances;

            _gridPresenter.SyncGridWithInstances(detectedInstances);
            _proxyPresenter.ProcessAutoConnect(detectedInstances, dataGridViewInstances);
        }

        private async void DataGridViewInstances_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (dataGridViewInstances.Columns[e.ColumnIndex].Name == "colAction")
            {
                var row = dataGridViewInstances.Rows[e.RowIndex];
                string action = row.Cells["colAction"].Value?.ToString();
                
                if (string.IsNullOrEmpty(action)) return; 

                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

                if (action == "Set Port")
                {
                    if (fixedPort > 0)
                    {
                        if (_validationService.IsPortDuplicate(fixedPort, dataGridViewInstances, e.RowIndex))
                        {
                            MessageBox.Show($"Port {fixedPort} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _configPresenter.UpdateAndSaveRule(row, _config);
                        _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
                    }
                    return;
                }

                if (action == "Start")
                {
                    int? pid = row.Tag as int?;
                    var instance = _currentInstances.FirstOrDefault(i => i.ProcessId == pid);

                    if (instance != null)
                    {
                        if (_validationService.IsPortDuplicate(fixedPort, dataGridViewInstances, e.RowIndex))
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

                        await _proxyPresenter.StartProxyAsync(instance, fixedPort, allowNetwork);
                        _configPresenter.UpdateAndSaveRule(row, _config);
                    }
                    else
                    {
                        MessageBox.Show("Power BI instance not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (action == "Stop")
                {
                    int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                    if (activeCount > 0)
                    {
                        var result = MessageBox.Show(
                            $"There are {activeCount} active connection(s) to this proxy.\nStopping it will disconnect them.\n\nAre you sure you want to stop?", 
                            "Active Connections Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        
                        if (result != DialogResult.Yes) return;
                    }

                    _proxyPresenter.StopProxy(fixedPort);
                }
                else if (action == "Remove")
                {
                    string status = row.Cells["colStatus"].Value?.ToString();
                    if (status == "Running")
                    {
                        MessageBox.Show("Cannot remove configuration while proxy is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string modelName = row.Cells["colModelName"].Value?.ToString();
                    var result = MessageBox.Show($"Remove configuration for '{modelName}'?", "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        _configPresenter.DeleteConfiguration(modelName, _config);
                        RefreshInstances();
                    }
                }
            }
        }

        private void DataGridViewInstances_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (dataGridViewInstances.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var validation = _validationService.ValidatePortAssignment(e.FormattedValue.ToString(), dataGridViewInstances, e.RowIndex);
                if (!validation.IsValid)
                {
                    e.Cancel = true;
                    dataGridViewInstances.Rows[e.RowIndex].ErrorText = validation.ErrorMessage;
                    if (validation.ErrorMessage.Contains("already assigned"))
                    {
                        MessageBox.Show(validation.ErrorMessage, "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    dataGridViewInstances.Rows[e.RowIndex].ErrorText = string.Empty;
                }
            }
        }

        private void DataGridViewInstances_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
             var row = dataGridViewInstances.Rows[e.RowIndex];
             _configPresenter.UpdateAndSaveRule(row, _config);
             
             if (row.Cells["colFixedPort"].Value != null && 
                 int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int port) && port > 0)
             {
                 if (!_proxyManager.IsRunning(port))
                 {
                     _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
                 }
             }
        }
        
        private void DataGridViewInstances_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                string colName = dataGridViewInstances.Columns[e.ColumnIndex].Name;
                if (colName == "colAuto" || colName == "colNetwork")
                {
                    _configPresenter.UpdateAndSaveRule(dataGridViewInstances.Rows[e.RowIndex], _config);
                }
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
                    while (_validationService.IsPortDuplicate(suggestedPort, dataGridViewInstances, e.RowIndex))
                    {
                        suggestedPort++;
                    }
                    row.Cells["colFixedPort"].Value = suggestedPort;
                    
                    _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
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
                    contextMenuStripGrid.Show(dataGridViewInstances, e.Location);
                }
            }
        }

        private void OpenFolder_Click(object sender, EventArgs e)
        {
            if (dataGridViewInstances.SelectedRows.Count > 0)
            {
                var row = dataGridViewInstances.SelectedRows[0];
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
            if (_proxyManager.GetRunningPorts().Any())
            {
                var result = MessageBox.Show("Proxies are currently running. Are you sure you want to exit?", "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _proxyPresenter.StopAll();
            _configPresenter.SaveConfiguration(_config);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized && checkBoxMinimizeToTray.Checked)
            {
                this.Hide();
                notifyIcon.Visible = true;
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        private void ToolStripMenuItemShow_Click(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        private void ToolStripMenuItemExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void ToolStripMenuItemCopy_Click(object sender, EventArgs e)
        {
            if (dataGridViewInstances.SelectedRows.Count > 0)
            {
                var row = dataGridViewInstances.SelectedRows[0];
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