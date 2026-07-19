using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using PBIPortWrapper.Presenters;
using PBIPortWrapper.Controls;

namespace PBIPortWrapper
{
    public partial class MainForm : Form
    {
        // Application Orchestration
        private ApplicationPresenter _appPresenter;
        
        // Presenters (Convenience accessors, or use _appPresenter.X)
        private GridPresenter _gridPresenter;
        private ProxyPresenter _proxyPresenter;
        private RowDetailsViewManager _rowDetailsManager;
        
        // State
        private List<PowerBIInstance> _currentInstances = new List<PowerBIInstance>();
        private HashSet<int> _expandedPids = new HashSet<int>();
        private ViewEventCoordinator _eventCoordinator;

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

            InitializeApplication();
            InitializeEventHandlers();
            InitializeContextMenu();
            
            // Initial refresh
            RefreshInstances();
        }

        private void InitializeApplication()
        {
            _appPresenter = new ApplicationPresenter(dataGridViewInstances);
            
            // Bind Presenters for local usage
            _gridPresenter = _appPresenter.GridPresenter;
            _proxyPresenter = _appPresenter.ProxyPresenter;

            // Bind UI Logging
            _appPresenter.LoggerService.OnLogMessage += (sender, args) => 
            {
                 UpdateLogDisplay(args.FormattedMessage);
            };

            // Detection state lives in the InstanceMonitor; the form only renders
            // snapshots, marshaled onto the UI thread.
            _appPresenter.Monitor.InstancesChanged += (s, args) =>
            {
                if (IsDisposed || Disposing) return;
                try
                {
                    BeginInvoke(new Action(() => ApplyInstances(args.Instances)));
                }
                catch (ObjectDisposedException) { /* form closed mid-scan */ }
                catch (InvalidOperationException) { /* handle not created yet or gone */ }
            };

            // Initial Log
            _appPresenter.LogAppInfo();
            
            // Initialize RowDetailsManager (Requires services)
            _rowDetailsManager = new RowDetailsViewManager(
                dataGridViewInstances,
                _appPresenter.ProxyManager,
                _appPresenter.ConfigService,
                _appPresenter.ServeSessionService,
                LogToService);
        }

        private void ConfigureGridColumns()
        {
            this.Text = "PBI Port Wrapper v0.5";

            // The designer's RowTemplate.Height is 96-DPI pixels, but fonts scale
            // with monitor DPI (PerMonitorV2) - rows must follow the font or the
            // text gets clipped on scaled displays.
            int rowHeight = dataGridViewInstances.Font.Height + 10;
            dataGridViewInstances.RowTemplate.Height = rowHeight;
            dataGridViewInstances.RowTemplate.MinimumHeight = rowHeight;

            // Add Expand Column
            if (!dataGridViewInstances.Columns.Contains("colExpand"))
            {
                var colExpand = new DataGridViewTextBoxColumn();
                colExpand.Name = "colExpand";
                colExpand.HeaderText = "";
                colExpand.ReadOnly = true;
                colExpand.Width = LogicalToDeviceUnits(30);
                colExpand.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dataGridViewInstances.Columns.Insert(0, colExpand);
            }

            // Add Active Connections Column
            if (!dataGridViewInstances.Columns.Contains("colActive"))
            {
                var colActive = new DataGridViewTextBoxColumn();
                colActive.Name = "colActive";
                colActive.HeaderText = "Active";
                colActive.ReadOnly = true;
                colActive.Width = LogicalToDeviceUnits(60);
                dataGridViewInstances.Columns.Add(colActive);
            }

            // Serve / Stop Serving action (#59)
            if (!dataGridViewInstances.Columns.Contains("colServe"))
            {
                var colServe = new DataGridViewButtonColumn();
                colServe.Name = "colServe";
                colServe.HeaderText = "Serve";
                colServe.UseColumnTextForButtonValue = false;
                dataGridViewInstances.Columns.Add(colServe);
            }

            dataGridViewInstances.Columns["colExpand"].DisplayIndex = 0;
            dataGridViewInstances.Columns["colModelName"].DisplayIndex = 1;
            dataGridViewInstances.Columns["colPbiPort"].DisplayIndex = 2;
            dataGridViewInstances.Columns["colFixedPort"].DisplayIndex = 3;
            dataGridViewInstances.Columns["colAuto"].DisplayIndex = 4;
            dataGridViewInstances.Columns["colNetwork"].DisplayIndex = 5;
            dataGridViewInstances.Columns["colAction"].DisplayIndex = 6;
            dataGridViewInstances.Columns["colServe"].DisplayIndex = 7;
            dataGridViewInstances.Columns["colStatus"].DisplayIndex = 8;
            dataGridViewInstances.Columns["colActive"].DisplayIndex = 9;

            dataGridViewInstances.Columns["colModelName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewInstances.Columns["colModelName"].FillWeight = 2.4f;

            dataGridViewInstances.Columns["colExpand"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dataGridViewInstances.Columns["colExpand"].Width = LogicalToDeviceUnits(30);

            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colAuto", "colNetwork", "colStatus", "colAction", "colActive" })
            {
                dataGridViewInstances.Columns[colName].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                dataGridViewInstances.Columns[colName].FillWeight = 1.0f;
            }

            // "Stop Serving" needs a wider button than the 1.0f columns
            dataGridViewInstances.Columns["colServe"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewInstances.Columns["colServe"].FillWeight = 1.3f;

            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colStatus", "colActive" })
            {
                dataGridViewInstances.Columns[colName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            foreach (var colName in new[] { "colAuto", "colNetwork", "colAction", "colServe" })
            {
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            
            buttonRefresh.Visible = false;
        }

        private void InitializeEventHandlers()
        {
            buttonOpenLogs.Click += ButtonOpenLogs_Click;

            var serveHandler = new ServeActionHandler(
                _appPresenter.ServeSessionService,
                _appPresenter.ConfigService,
                () => _currentInstances,
                LogToService);

            _eventCoordinator = new ViewEventCoordinator(
                dataGridViewInstances,
                contextMenuStripGrid,
                _appPresenter.ValidationService,
                _gridPresenter,
                () => _currentInstances,
                (port) => _appPresenter.ProxyManager.IsRunning(port),
                RefreshInstances,
                ToggleRowExpansion,
                serveHandler
            );

            // Wire up Domain Events from View
            _eventCoordinator.ConfigRequested += (s, args) => 
            {
                _appPresenter.ConfigService.UpdateRule(args.ModelName, args.FixedPort, args.Auto, args.AllowNetwork);
            };

            _eventCoordinator.ActionRequested += async (s, args) =>
            {
                switch (args.Action)
                {
                    case RowActionType.Start:
                        await _appPresenter.ProxyPresenter.StartProxyAsync(args.Instance, args.FixedPort, args.AllowNetwork);
                        break;

                    case RowActionType.Stop:
                        int activeCount = _appPresenter.ProxyManager.GetActiveConnections(args.FixedPort);
                        if (activeCount > 0)
                        {
                            var result = MessageBox.Show(
                                $"There are {activeCount} active connection(s) to this proxy.\nStopping it will disconnect them.\n\nAre you sure you want to stop?",
                                "Active Connections Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                            if (result != DialogResult.Yes) return;
                        }
                        _appPresenter.ProxyPresenter.StopProxy(args.FixedPort, args.Instance?.WorkspaceId);
                        break;

                    case RowActionType.Remove:
                        _appPresenter.ConfigService.RemoveRule(args.ModelName);
                        break;
                }
            };
            
            dataGridViewInstances.CellContentClick += _eventCoordinator.OnCellContentClick;
            dataGridViewInstances.CellValueChanged += _eventCoordinator.OnCellValueChanged;
            dataGridViewInstances.CellEndEdit += _eventCoordinator.OnCellEndEdit;
            dataGridViewInstances.CellValidating += _eventCoordinator.OnCellValidating;
            dataGridViewInstances.CellEnter += _eventCoordinator.OnCellEnter;
            
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
            // Snapshots arriving before the handle exists are dropped by BeginInvoke;
            // request a fresh scan once the form is actually visible.
            this.Shown += (s, e) => RefreshInstances();
            
            checkBoxMinimizeToTray.CheckedChanged += (s, e) => 
            {
                if (_appPresenter.ConfigService.Current != null)
                {
                    _appPresenter.ConfigService.SetMinimizeToTray(checkBoxMinimizeToTray.Checked);
                }
            };
            
            if (_appPresenter.ConfigService.Current != null)
            {
                checkBoxMinimizeToTray.Checked = _appPresenter.ConfigService.Current.MinimizeToTray;
            }
        }

        private void InitializeContextMenu()
        {
            // "Workspace", not "Folder"/"Path": these point at the AS workspace
            // dir, not the .pbix location (#59 polish)
            var openFolderItem = new ToolStripMenuItem("Open Workspace Folder");
            openFolderItem.Click += _eventCoordinator.ContextMenuHandler.OnOpenFolderClick;
            contextMenuStripGrid.Items.Add(openFolderItem);

            var copyPathItem = new ToolStripMenuItem("Copy Workspace Path");
            copyPathItem.Click += _eventCoordinator.ContextMenuHandler.OnCopyPathClick;
            contextMenuStripGrid.Items.Add(copyPathItem);
            
            dataGridViewInstances.ContextMenuStrip = contextMenuStripGrid;
            dataGridViewInstances.MouseDown += _eventCoordinator.OnMouseDown;
        }

        private void ToggleRowExpansion(int pid)
        {
            if (_expandedPids.Contains(pid)) _expandedPids.Remove(pid);
            else _expandedPids.Add(pid);
            // Use cached data for instant UI updates (avoids slow WMI re-scan)
            _gridPresenter.RefreshGrid(_currentInstances, _appPresenter.ConfigService.Current, _expandedPids);
            _rowDetailsManager.SyncDetailsPanels(_currentInstances, _expandedPids);
        }

        private void RefreshInstances()
        {
            _appPresenter.Monitor.RequestRefresh();
        }

        private void ApplyInstances(IReadOnlyList<PowerBIInstance> instances)
        {
            if (IsDisposed || Disposing) return;

            try
            {
                _currentInstances = instances.ToList();
                _gridPresenter.RefreshGrid(_currentInstances, _appPresenter.ConfigService.Current, _expandedPids);
                _rowDetailsManager.SyncDetailsPanels(_currentInstances, _expandedPids);
                _proxyPresenter.ProcessAutoConnect(_currentInstances, _appPresenter.ConfigService.Current);
                _appPresenter.ServeSessionService.OnInstancesChanged(_currentInstances);
                _appPresenter.ServeRecovery.OnSnapshot(_currentInstances);
            }
            catch (Exception ex)
            {
                LogToService($"Error applying instance snapshot: {ex.Message}");
            }
        }

        private void ButtonOpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                string logFile = _appPresenter.ConfigManager.GetLogFilePath();
                if (File.Exists(logFile)) System.Diagnostics.Process.Start("notepad.exe", logFile);
                else MessageBox.Show("Log file does not exist yet.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_appPresenter.ProxyManager.HasRunningProxies())
            {
                var result = MessageBox.Show(
                    "There are active Power BI proxies running.\nClosing the application will stop them.\n\nAre you sure you want to exit?",
                    "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _appPresenter.StopAll();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && 
                _appPresenter.ConfigService.Current != null && 
                _appPresenter.ConfigService.Current.MinimizeToTray)
            {
                Hide();
                notifyIcon.Visible = true;
                notifyIcon.ShowBalloonTip(3000, "PBI Port Wrapper", "Minimised to tray", ToolTipIcon.Info);
            }
        }
        
        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) => ShowFromTray();

        private void NotifyIcon_DoubleClick(object sender, EventArgs e) => ShowFromTray();

        private void ToolStripMenuItemShow_Click(object sender, EventArgs e) => ShowFromTray();

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        private void ToolStripMenuItemExit_Click(object sender, EventArgs e) => this.Close();

        private void ToolStripMenuItemCopy_Click(object sender, EventArgs e) =>
            _eventCoordinator.ContextMenuHandler.OnCopyConnectionStringClick(sender, e);

        private void LogToService(string message)
        {
           _appPresenter?.LoggerService?.LogInfo("App", message);
        }

        private void UpdateLogDisplay(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateLogDisplay(message)));
                return;
            }
            textBoxLog.AppendText($"{message}{Environment.NewLine}");
        }

    }
}