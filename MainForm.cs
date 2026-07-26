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
            AddEndpointSettingsButton();
            
            // Initial refresh
            RefreshInstances();
        }

        private void InitializeApplication()
        {
            _appPresenter = new ApplicationPresenter(dataGridViewInstances);
            
            // Bind Presenters for local usage
            _gridPresenter = _appPresenter.GridPresenter;

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
                _appPresenter.ConfigService,
                _appPresenter.ServeSessionService,
                EndpointUrlFor,
                LogToService);
        }

        /// <summary>
        /// A served model's address on the endpoint, or empty when it is not reachable.
        /// The details panel shows connection details only when they would actually
        /// connect (#126).
        /// </summary>
        private string EndpointUrlFor(string alias)
        {
            var status = _appPresenter.Endpoint?.Status;
            var config = _appPresenter.ConfigService.Current?.HttpBridge;
            if (status == null || config == null || !status.Running || string.IsNullOrWhiteSpace(alias))
                return string.Empty;

            return EndpointUrlBuilder.For(
                ConnectionEndpoint.EndpointHost(config, status), status.Port, alias);
        }

        /// <summary>The connection string for a served model, or empty when it is not
        /// reachable. Same source as the tray's Copy connection string.</summary>
        private string ConnectionStringFor(string alias) =>
            ConnectionStringBuilder.ForEndpoint(EndpointUrlFor(alias), alias);

        private void ConfigureGridColumns()
        {
            this.Text = Presenters.ApplicationPresenter.AppTitle;
            Presenters.GridColumnLayout.Apply(dataGridViewInstances, LogicalToDeviceUnits);
            buttonRefresh.Visible = false;
        }

        /// <summary>
        /// The dashboard's way into the endpoint's typed settings (#125). Added in code
        /// rather than the designer: the tray carries the switches, so this is one
        /// button, and keeping it here avoids a designer round trip for one control.
        /// </summary>
        private void AddEndpointSettingsButton()
        {
            var button = new Button
            {
                Text = "XMLA endpoint…",
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(LogicalToDeviceUnits(400), LogicalToDeviceUnits(20))
            };
            button.Click += (s, e) =>
            {
                using (var dialog = new Controls.EndpointSettingsDialog(
                           _appPresenter.ConfigService, _appPresenter.Endpoint))
                {
                    dialog.ShowDialog(this);
                }
            };
            panelTop.Controls.Add(button);
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
                _appPresenter.ServeSessionService,
                serveHandler, _appPresenter.ConfigService,
                _appPresenter.Endpoint,
                ShowFromTray, () => this.Close());

            _toasts = new TrayToastService(notifyIcon, ConnectionStringFor);
            _lifecycle = new ServeLifecycleCoordinator(
                _appPresenter.ServeSessionService, _appPresenter.ConfigService,
                _toasts, ShowFromTray, LogToService);

            _eventCoordinator = new ViewEventCoordinator(
                dataGridViewInstances,
                contextMenuStripGrid,
                _gridPresenter,
                () => _currentInstances,
                RefreshInstances,
                ToggleRowExpansion,
                serveHandler,
                _appPresenter.ConfigService.FindRule,
                ws => _appPresenter.ServeSessionService.FindSession(ws) != null
            );

            // Reflect config edits (e.g. a grid policy change) in the tray immediately.
            // ConfigurationChanged can fire from a background serve, so marshal to the UI.
            _appPresenter.ConfigService.ConfigurationChanged += (s, e) => RebuildTrayOnUiThread();

            // The endpoint's status is not a configuration value - a bind can fail, or
            // fall back to localhost - and it is applied after the configuration change
            // that caused it. Subscribing here as well means the tray shows the outcome
            // without depending on the order two handlers happen to run in.
            _appPresenter.Endpoint.StatusChanged += (s, e) => RebuildTrayOnUiThread();

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
            };

            // The alias is free text, so it commits when the user leaves the cell
            // rather than on every keystroke. It is validated first: an invalid alias
            // would be rejected by the rename anyway, and finding that out at serve
            // time is far worse than being told here (#126).
            dataGridViewInstances.CellEndEdit += (s, e) =>
            {
                var grid = dataGridViewInstances;
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "colAlias") return;

                var row = grid.Rows[e.RowIndex];
                string modelName = row.Cells["colModelName"].Value?.ToString();
                if (string.IsNullOrEmpty(modelName)) return;

                string alias = row.Cells["colAlias"].Value?.ToString()?.Trim() ?? string.Empty;
                var rule = _appPresenter.ConfigService.FindRule(modelName);
                if (alias == (rule?.StableAlias ?? string.Empty)) return;

                if (alias.Length > 0)
                {
                    var (isValid, error) = AliasValidator.ValidateAlias(alias);
                    if (!isValid)
                    {
                        MessageBox.Show(error, "Alias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.Cells["colAlias"].Value = rule?.StableAlias;   // put the old one back
                        return;
                    }
                }

                _appPresenter.ConfigService.SetStableAlias(modelName, alias);
            };

            // Never let a stray combobox value mismatch surface as a modal error dialog.
            dataGridViewInstances.DataError += (s, e) => e.ThrowException = false;

            _eventCoordinator.ActionRequested += async (s, args) =>
            {
                switch (args.Action)
                {
                    case RowActionType.Remove:
                        _appPresenter.ConfigService.RemoveRule(args.ModelName);
                        break;
                }
            };
            
            dataGridViewInstances.CellContentClick += _eventCoordinator.OnCellContentClick;
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

            // Only serving is worth confirming now: it renames real databases, and
            // exiting puts those names back. Nothing else is left running to warn about.
            if (_appPresenter.ServeSessionService.ActiveSessions.Count > 0)
            {
                const string message =
                    "Models are being served. Exiting will restore their original database names " +
                    "and they will stop answering on the XMLA endpoint.\n\nExit now?";

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

        /// <summary>
        /// Rebuilds the tray from whichever thread notices a change. Serve completions
        /// and endpoint restarts both arrive off the UI thread, and the menu is a UI
        /// object, so every path marshals through here.
        /// </summary>
        private void RebuildTrayOnUiThread()
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(new Action(() => _trayMenu?.Rebuild(_currentInstances))); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void ToolStripMenuItemShow_Click(object sender, EventArgs e) => ShowFromTray();

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            // Tray-first: the icon stays visible whether or not the window is open.
            Activate();
        }

        private void ToolStripMenuItemExit_Click(object sender, EventArgs e) => this.Close();

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