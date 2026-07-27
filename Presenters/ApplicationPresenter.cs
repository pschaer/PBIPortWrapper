using System;
using System.Collections.Generic;
using System.IO;
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
        public ConfigurationManager ConfigManager { get; private set; }
        public DatabaseRenameService RenameService { get; private set; }
        
        // Services
        public ConfigService ConfigService { get; private set; }
        public ServeSessionService ServeSessionService { get; private set; }
        public HttpBridgeService HttpBridge { get; private set; }

        /// <summary>The endpoint's access log (#128); the tray opens it by path.</summary>
        public AccessLog AccessLog { get; private set; }

        /// <summary>Owns the endpoint's lifetime and its status (#125).</summary>
        public XmlaEndpointCoordinator Endpoint { get; private set; }

        // Presenters
        public GridPresenter GridPresenter { get; private set; }
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
            RenameService = new DatabaseRenameService(LoggerService);
            Monitor = new InstanceMonitor(Detector, LogToService);
            ConfigService = new ConfigService(ConfigManager);
            ConfigService.Load();
            // Preflight (#59): UIA undo-heuristic probe; Clean lets serve start
            // silently, MaybeDirty/Unknown make the UI ask the user.
            ServeSessionService = new ServeSessionService(
                RenameService, ConfigService, new UiaDirtyStateProbe(LogToService), LoggerService);

            // XMLA endpoint (#77): stays off unless enabled in config. The coordinator
            // owns its lifetime from here on, so settings can change while running
            // (#125); binding failures surface as status and are never fatal — the
            // wrapper's core job must not depend on it.
            // access.csv sits beside log.txt, so the two are found together and the
            // app keeps one idea of where it writes things (#128).
            AccessLog = new AccessLog(
                Path.Combine(Path.GetDirectoryName(LoggerService.GetLogFilePath()) ?? string.Empty, "access.csv"),
                onNotice: message => LogToService(message));

            HttpBridge = new HttpBridgeService(
                new XmlaRelay(ServedCatalogs, LoggerService), LoggerService, AccessLog);
            Endpoint = new XmlaEndpointCoordinator(HttpBridge, ConfigService, LoggerService);
            Endpoint.StatusChanged += (_, status) => LogToService($"XMLA endpoint: {status.Summary}");
            Endpoint.ApplyConfiguration();
        }

        /// <summary>
        /// Every model currently reachable through the XMLA endpoint. Serving is what
        /// puts a model on this list: it renames the workspace database to the stable
        /// alias at the source, so the alias is both the path a client addresses and
        /// the catalog msmdsrv already has. Nothing to rewrite anywhere.
        /// </summary>
        private IReadOnlyList<ServedCatalog> ServedCatalogs()
        {
            return ServeSessionService.ActiveSessions
                .Where(s => !string.IsNullOrWhiteSpace(s.Alias))
                .OrderBy(s => s.Alias, StringComparer.OrdinalIgnoreCase)
                // Read-only travels with the catalog so the relay can refuse a mutating
                // Execute without knowing anything about rules or sessions (#129). A
                // model with no rule yet defaults to read-only, like a new one does.
                .Select(s => new ServedCatalog(
                    s.Alias, s.InstancePort, ConfigService.FindRule(s.FileName)?.ReadOnly ?? true))
                .ToList();
        }

        private void InitializePresenters(DataGridView grid)
        {
            ServeRecovery = new ServeRecoveryCoordinator(
                ServeSessionService, RenameService, ConfigService, grid, LogToService);
            
            GridPresenter = new GridPresenter(
                grid,
                ConfigService.Current,
                ServeSessionService.FindSession,
                ConfigService.FindRule,
                LogToService);
        }

        private void WireUpInternalEvents()
        {
            // Serve sessions own their rows' painting (#59): repaint on start/end
            // so "Serving" appears and clears without waiting for the next scan.
            ServeSessionService.SessionStarted += (sender, args) => GridPresenter?.RepaintAllRows();
            ServeSessionService.SessionEnded += (sender, args) => GridPresenter?.RepaintAllRows();

            // Alias edits change Serve availability; repaint when config is saved.
            ConfigService.ConfigurationChanged += (sender, args) => GridPresenter?.RepaintAllRows();        }

        /// <summary>
        /// Window/log title with the version derived from the assembly, so a release
        /// version bump updates it automatically (no hardcoded "vX.Y" to go stale — #113).
        /// </summary>
        public static string AppTitle
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"PBI Port Wrapper v{version?.Major}.{version?.Minor}";
            }
        }

        public void LogAppInfo()
        {
            LogToService(AppTitle);
            LogToService("Features: Multi-instance support, Auto-reconnect, Offline config management");
            LogToService($"Log file: {LoggerService?.GetLogFilePath()}"); 
            LogToService("");
        }

        private void LogToService(string message)
        {
             LoggerService?.LogInfo("App", message);
        }

        /// <summary>
        /// Stops detection and all proxies. Served-database restoration is owned by
        /// <see cref="ServeLifecycleCoordinator.OnAppExitAsync"/> and runs first, on
        /// exit (#100); this only tears down the infrastructure.
        /// </summary>
        public void StopAll()
        {
            Monitor?.Dispose();
            Endpoint?.Dispose();   // unsubscribes, then stops the listener
        }
    }
}
