using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private LoggerService _loggerService;
        private ProxyConfiguration _config;
        private FileSystemWatcher _portFileWatcher;
        
        // Presenters
        private GridPresenter _gridPresenter;
        private ProxyPresenter _proxyPresenter;
        private ConfigPresenter _configPresenter;
        private ViewEventCoordinator _eventCoordinator;
        
        // State
        private List<PowerBIInstance> _currentInstances = new List<PowerBIInstance>();

        public MainForm()
        {
            InitializeComponent();
            ConfigureGridColumns(); 
            
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
            this.Text = "PBI Port Wrapper v0.3";

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

            // Issue #8: Make Model Name column roughly twice as large as other columns
            dataGridViewInstances.Columns["colModelName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewInstances.Columns["colModelName"].FillWeight = 2.4f;
            
            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colAuto", "colNetwork", "colStatus", "colAction", "colActive" })
            {
                dataGridViewInstances.Columns[colName].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                dataGridViewInstances.Columns[colName].FillWeight = 1.0f;
            }

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

            LogMessage("PBI Port Wrapper v0.3");
            LogMessage("Features: Multi-instance support, Auto-reconnect, Offline config management");
            LogMessage($"Log file: {_loggerService?.GetLogFilePath()}"); 
            LogMessage("");
        }

                        private void InitializeServices()
        {
            _detector = new PowerBIDetector();
            _loggerService = new LoggerService(LogLevel.Info);
            _proxyManager = new ProxyManager(_loggerService);
            _configManager = new ConfigurationManager();
            _validationService = new ValidationService();
            
            _loggerService.OnLogMessage += (sender, args) => 
            {
                if (args.Level >= LogLevel.Warning)
                {
                    LogMessage(args.Message);
                }
            };

            _proxyManager.OnLog += (sender, message) => LogMessage(message);
            _proxyManager.OnError += (sender, message) => LogMessage($"ERROR: {message}");
            
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

            InitializePortFileWatcher();
        }

        private void InitializePortFileWatcher()
        {
            try
            {
                string workspacesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Power BI Desktop\AnalysisServicesWorkspaces"
                );

                if (!Directory.Exists(workspacesPath))
                    return;

                _portFileWatcher = new FileSystemWatcher(workspacesPath);
                _portFileWatcher.Filter = "*.port.txt";
                _portFileWatcher.IncludeSubdirectories = true;
                _portFileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

                _portFileWatcher.Created += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[PortFileWatcher] Port file created: {e.Name}");
                    Task.Delay(500).ContinueWith(_ => RefreshInstances());
                };

                _portFileWatcher.Changed += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[PortFileWatcher] Port file changed: {e.Name}");
                    Task.Delay(500).ContinueWith(_ => RefreshInstances());
                };

                _portFileWatcher.EnableRaisingEvents = true;
                LogMessage("Port file watcher initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing port file watcher: {ex.Message}");
            }
        }

        private void InitializePresenters()
        {
            _configPresenter = new ConfigPresenter(_configManager, LogMessage);
            _config = _configPresenter.LoadConfiguration(); 

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

            _eventCoordinator = new ViewEventCoordinator(
                dataGridViewInstances,
                contextMenuStripGrid,
                _proxyManager,
                _validationService,
                _gridPresenter,
                _proxyPresenter,
                _configPresenter,
                () => _config,
                () => _currentInstances,
                RefreshInstances
            );
            
            dataGridViewInstances.CellContentClick += _eventCoordinator.OnCellContentClick;
            dataGridViewInstances.CellValueChanged += _eventCoordinator.OnCellValueChanged;
            dataGridViewInstances.CellEndEdit += _eventCoordinator.OnCellEndEdit;
            dataGridViewInstances.CellValidating += _eventCoordinator.OnCellValidating;
            dataGridViewInstances.CellEnter += _eventCoordinator.OnCellEnter;
            
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
            openFolderItem.Click += _eventCoordinator.ContextMenuHandler.OnOpenFolderClick;
            contextMenuStripGrid.Items.Add(openFolderItem);

            var copyPathItem = new ToolStripMenuItem("Copy Path");
            copyPathItem.Click += _eventCoordinator.ContextMenuHandler.OnCopyPathClick;
            contextMenuStripGrid.Items.Add(copyPathItem);
            
            dataGridViewInstances.ContextMenuStrip = contextMenuStripGrid;
            dataGridViewInstances.MouseDown += _eventCoordinator.OnMouseDown;
        }

                        private void RefreshInstances()
        {
            if (!_detector.IsWorkspacePathValid()) return;

            var detectedInstances = _detector.DetectRunningInstances();
            _currentInstances = detectedInstances;

            _gridPresenter.RefreshGrid(detectedInstances, _config);
            _proxyPresenter.ProcessAutoConnect(detectedInstances, dataGridViewInstances);
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
            if (_portFileWatcher != null)
            {
                _portFileWatcher.EnableRaisingEvents = false;
                _portFileWatcher.Dispose();
            }

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
            _eventCoordinator.ContextMenuHandler.OnCopyConnectionStringClick(sender, e);
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