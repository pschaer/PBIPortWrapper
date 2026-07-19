using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBIPortWrapper.Services;
using PBIPortWrapper.Models;
// using PBIPortWrapper.Forms; // No longer needed here as RowDetails is now managed by ViewManager

namespace PBIPortWrapper.Presenters
{
    public class ApplicationPresenter
    {
        // Core Services
        public PowerBIDetector Detector { get; private set; }
        public InstanceMonitor Monitor { get; private set; }
        public LoggerService LoggerService { get; private set; }
        public ProxyManager ProxyManager { get; private set; }
        public ConfigurationManager ConfigManager { get; private set; }
        public ValidationService ValidationService { get; private set; }
        public DatabaseRenameService RenameService { get; private set; }
        
        // Services
        public ConfigService ConfigService { get; private set; }
        public ServeSessionService ServeSessionService { get; private set; }

        // Presenters
        public GridPresenter GridPresenter { get; private set; }
        public ProxyPresenter ProxyPresenter { get; private set; }
        public ServeRecoveryCoordinator ServeRecovery { get; private set; }




        public ApplicationPresenter(DataGridView grid)
        {
            InitializeServices();
            InitializePresenters(grid);
            WireUpInternalEvents();
        }

        private void InitializeServices()
        {
            Detector = new PowerBIDetector();
            LoggerService = new LoggerService(LogLevel.Info);
            ConfigManager = new ConfigurationManager();
            ValidationService = new ValidationService();
            ProxyManager = new ProxyManager(LoggerService);
            RenameService = new DatabaseRenameService(LoggerService);
            Monitor = new InstanceMonitor(Detector, LogToService);
            ConfigService = new ConfigService(ConfigManager);
            ConfigService.Load();
            // Preflight (#59): UIA undo-heuristic probe; Clean lets serve start
            // silently, MaybeDirty/Unknown make the UI ask the user.
            ServeSessionService = new ServeSessionService(
                RenameService, ProxyManager, ConfigService, new UiaDirtyStateProbe(), LoggerService);
        }

        private void InitializePresenters(DataGridView grid)
        {
            ProxyPresenter = new ProxyPresenter(ProxyManager, LogToService);
            ServeRecovery = new ServeRecoveryCoordinator(
                ServeSessionService, RenameService, ConfigService, grid, LogToService);
            
            GridPresenter = new GridPresenter(
                grid,
                ProxyManager,
                ValidationService,
                ConfigService.Current,
                ServeSessionService.FindSession,
                ConfigService.FindRule,
                LogToService);
        }

        private void WireUpInternalEvents()
        {
            // Proxy Status Updates -> Grid
            ProxyManager.OnProxyStarted += (sender, args) =>
            {
                GridPresenter?.UpdateGridStatus(args.FixedPort);
                LogToService($"Started proxy on port {args.FixedPort} -> {args.TargetPort}");
            };

            ProxyManager.OnProxyStopped += (sender, args) =>
            {
                GridPresenter?.UpdateGridStatus(args.FixedPort);
                LogToService($"Stopped proxy on port {args.FixedPort}");
            };

            // Serve sessions own their rows' painting (#59): repaint on start/end
            // so "Serving" appears and clears without waiting for the next scan.
            ServeSessionService.SessionStarted += (sender, args) => GridPresenter?.RepaintAllRows();
            ServeSessionService.SessionEnded += (sender, args) => GridPresenter?.RepaintAllRows();

            // Alias edits change Serve availability; repaint when config is saved.
            ConfigService.ConfigurationChanged += (sender, args) => GridPresenter?.RepaintAllRows();

            ProxyManager.OnProxyConnectionCountChanged += (sender, args) =>
            {
                GridPresenter?.UpdateActiveConnections(args.FixedPort, args.Count);
            };
        }

        public void LogAppInfo()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            LogToService($"PBI Port Wrapper v{version?.Major}.{version?.Minor}");
            LogToService("Features: Multi-instance support, Auto-reconnect, Offline config management");
            LogToService($"Log file: {LoggerService?.GetLogFilePath()}"); 
            LogToService("");
        }

        private void LogToService(string message)
        {
             LoggerService?.LogInfo("App", message);
        }

        public void StopAll()
        {
            Monitor?.Dispose();
            ProxyManager?.StopAll();
        }
    }
}
