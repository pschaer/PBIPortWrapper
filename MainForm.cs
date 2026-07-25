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
        private TrayMenuManager _trayMenu;
        private TrayToastService _toasts;
        private ServeLifecycleCoordinator _lifecycle;
        private bool _shuttingDown;
        private bool _shutdownComplete;
        private readonly bool _startSilent;

        public MainForm(bool startSilent = false)
        {
            _startSilent = startSilent;
            InitializeComponent();
            ConfigureGridColumns();
            
            // App icon (optional); loader keeps this form under its size limit.
            var appIcon = AppIconLoader.TryLoad();
            if (appIcon != null)
            {
                this.Icon = appIcon;
                this.notifyIcon.Icon = appIcon;
            }

            // #87: when started with --silent (auto-start at login), begin
            // minimized so the form never flashes before hiding to the tray.
            if (_startSilent)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }

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
            this.Text = Presenters.ApplicationPresenter.AppTitle;

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

            // On-detection policy dropdown (#88): same choices, in the same order, as
            // the tray "On detection" submenu, so grid and tray read identically.
            var onDetectionCol = (DataGridViewComboBoxColumn)dataGridViewInstances.Columns["colOnDetection"];
            onDetectionCol.Items.Clear();
            foreach (var policy in OnDetectionPolicyLabel.Order)
                onDetectionCol.Items.Add(OnDetectionPolicyLabel.For(policy));
            onDetectionCol.FlatStyle = FlatStyle.Flat;

            dataGridViewInstances.Columns["colExpand"].DisplayIndex = 0;
            dataGridViewInstances.Columns["colModelName"].DisplayIndex = 1;
            dataGridViewInstances.Columns["colPbiPort"].DisplayIndex = 2;
            dataGridViewInstances.Columns["colFixedPort"].DisplayIndex = 3;
            dataGridViewInstances.Columns["colOnDetection"].DisplayIndex = 4;
            dataGridViewInstances.Columns["colNetwork"].DisplayIndex = 5;
            dataGridViewInstances.Columns["colAction"].DisplayIndex = 6;
            dataGridViewInstances.Columns["colStatus"].DisplayIndex = 7;
            dataGridViewInstances.Columns["colActive"].DisplayIndex = 8;

            dataGridViewInstances.Columns["colModelName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewInstances.Columns["colModelName"].FillWeight = 2.4f;

            dataGridViewInstances.Columns["colExpand"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dataGridViewInstances.Columns["colExpand"].Width = LogicalToDeviceUnits(30);

            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colNetwork", "colStatus", "colAction", "colActive" })
            {
                dataGridViewInstances.Columns[colName].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                dataGridViewInstances.Columns[colName].FillWeight = 1.0f;
            }

            // The policy labels ("Serve after grace period") are wide; give the dropdown room.
            dataGridViewInstances.Columns["colOnDetection"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewInstances.Columns["colOnDetection"].FillWeight = 2.0f;

            foreach (var colName in new[] { "colPbiPort", "colFixedPort", "colStatus", "colActive" })
            {
                dataGridViewInstances.Columns[colName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dataGridViewInstances.Columns[colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            foreach (var colName in new[] { "colOnDetection", "colNetwork", "colAction" })
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

            _trayMenu = new TrayMenuManager(
                notifyIcon, contextMenuStripTray,
                _appPresenter.ProxyManager, _appPresenter.ServeSessionService,
                _proxyPresenter, serveHandler, _appPresenter.ConfigService,
                ShowFromTray, () => this.Close());

            _toasts = new TrayToastService(notifyIcon);
            _lifecycle = new ServeLifecycleCoordinator(
                _appPresenter.ServeSessionService, _appPresenter.ConfigService,
                _toasts, ShowFromTray, LogToService);

            _eventCoordinator = new ViewEventCoordinator(
                dataGridViewInstances,
                contextMenuStripGrid,
                _appPresenter.ValidationService,
                _gridPresenter,
                () => _currentInstances,
                (port) => _appPresenter.ProxyManager.IsRunning(port),
                RefreshInstances,
                ToggleRowExpansion,
                serveHandler,
                _appPresenter.ConfigService.FindRule,
                ws => _appPresenter.ServeSessionService.FindSession(ws) != null
            );

            // Wire up Domain Events from View
            _eventCoordinator.ConfigRequested += (s, args) =>
            {
                _appPresenter.ConfigService.UpdateRule(args.ModelName, args.FixedPort, args.Auto, args.AllowNetwork);
            };

            // Reflect config edits (e.g. a grid policy change) in the tray immediately.
            // ConfigurationChanged can fire from a background serve, so marshal to the UI.
            _appPresenter.ConfigService.ConfigurationChanged += (s, e) =>
            {
                if (IsDisposed || Disposing) return;
                try { BeginInvoke(new Action(() => _trayMenu?.Rebuild(_currentInstances))); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };

            // Grid On-detection dropdown / Network checkbox -> config (#88). Persist on
            // the user's commit (the cell going dirty), NOT on CellValueChanged - so the
            // display-only projection in RowStatusPainter never writes config. Commit
            // immediately so the change registers on the first click, not the next one.
            dataGridViewInstances.CurrentCellDirtyStateChanged += (s, e) =>
            {
                var cell = dataGridViewInstances.CurrentCell;
                if (cell == null || !dataGridViewInstances.IsCurrentCellDirty) return;
                string modelName = dataGridViewInstances.Rows[cell.RowIndex].Cells["colModelName"].Value?.ToString();
                if (string.IsNullOrEmpty(modelName)) return;

                if (cell is DataGridViewComboBoxCell && cell.OwningColumn?.Name == "colOnDetection")
                {
                    dataGridViewInstances.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    if (OnDetectionPolicyLabel.TryParse(cell.Value?.ToString(), out var policy))
                        _appPresenter.ConfigService.SetOnDetection(modelName, policy);
                }
                else if (cell is DataGridViewCheckBoxCell && cell.OwningColumn?.Name == "colNetwork")
                {
                    // A checkbox's committed Value still holds the OLD state here; the new
                    // state is the pending edit. Read that, or the toggle reads backwards
                    // and the projection snaps it back (couldn't de-select network).
                    bool newValue = Convert.ToBoolean(cell.EditedFormattedValue);
                    dataGridViewInstances.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    _appPresenter.ConfigService.SetNetwork(modelName, newValue);
                }
            };

            // Never let a stray combobox value mismatch surface as a modal error dialog.
            dataGridViewInstances.DataError += (s, e) => e.ThrowException = false;

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
            dataGridViewInstances.CellEndEdit += _eventCoordinator.OnCellEndEdit;
            dataGridViewInstances.CellValidating += _eventCoordinator.OnCellValidating;
            dataGridViewInstances.CellEnter += _eventCoordinator.OnCellEnter;
            
            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
            this.Shown += MainForm_Shown;

            // Settings checkboxes + auto-start reconcile (#87).
            SettingsBinder.Bind(checkBoxMinimizeToTray, checkBoxStartWithWindows, _appPresenter.ConfigService);
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
                _trayMenu?.Rebuild(_currentInstances);
                _lifecycle?.OnSnapshot(_currentInstances);
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
            if (_shutdownComplete) return;              // shutdown finished; let this close proceed
            if (_shuttingDown) { e.Cancel = true; return; } // shutdown already in progress

            bool serving = _appPresenter.ServeSessionService.ActiveSessions.Count > 0;

            if (_appPresenter.ProxyManager.HasRunningProxies())
            {
                string message = serving
                    ? "Models are being served. Exiting will restore their original database names and stop forwarding.\n\nExit now?"
                    : "There are active Power BI proxies running.\nClosing the application will stop them.\n\nAre you sure you want to exit?";

                if (MessageBox.Show(message, "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Restore served databases before exit (#100). Do it asynchronously: the
            // teardown raises events whose handlers marshal back to the UI thread, so
            // blocking here would deadlock. Cancel this close, hide the window, and
            // close for real once shutdown finishes.
            e.Cancel = true;
            _shuttingDown = true;
            Hide();
            _ = ShutdownThenCloseAsync();
        }

        private async Task ShutdownThenCloseAsync()
        {
            // Restore served databases first (the coordinator owns exit now, #100),
            // then stop detection and proxies. Each step is isolated so a restore
            // failure still tears the proxies down.
            try { await _lifecycle.OnAppExitAsync(); }
            catch (Exception ex) { LogToService($"Exit restore error: {ex.Message}"); }
            try { _appPresenter.StopAll(); }
            catch (Exception ex) { LogToService($"Shutdown error: {ex.Message}"); }
            _shutdownComplete = true;
            Close();
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
            // Tray-first: the icon stays visible whether or not the window is open.
            Activate();
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

        // #87: on Shown, complete silent-start (hide to tray) and kick a refresh.
        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (_startSilent) { Hide(); notifyIcon.Visible = true; ShowInTaskbar = true; }
            RefreshInstances();
        }
    }
}