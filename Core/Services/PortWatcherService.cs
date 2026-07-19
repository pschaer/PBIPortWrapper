using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PBIPortWrapper.Services
{
    public class PortWatcherService : IDisposable
    {
        private FileSystemWatcher _portFileWatcher;
        private CancellationTokenSource _refreshDebounceCts;
        private System.Threading.Timer _retryTimer;
        private readonly Action<string> _logCallback;
        private readonly string _workspacesPath;
        private readonly int _retryIntervalMs;
        private readonly object _watcherLock = new object();
        private bool _isDisposed;

        public event EventHandler OnRefreshNeeded;

        public bool IsWatching
        {
            get { lock (_watcherLock) { return _portFileWatcher?.EnableRaisingEvents == true; } }
        }

        public PortWatcherService(Action<string> logCallback, string workspacesPath = null, int retryIntervalMs = 5000)
        {
            _logCallback = logCallback;
            _retryIntervalMs = retryIntervalMs;
            _workspacesPath = workspacesPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Power BI Desktop\AnalysisServicesWorkspaces"
            );

            if (!TryInitializeWatcher())
            {
                // Workspaces dir does not exist yet (Desktop never run on this machine,
                // or fresh profile) - retry until it appears instead of staying dead (#51).
                StartRetryTimer();
            }
        }

        private bool TryInitializeWatcher()
        {
            lock (_watcherLock)
            {
                if (_isDisposed) return false;
                if (_portFileWatcher != null) return true;

                try
                {
                    if (!Directory.Exists(_workspacesPath))
                        return false;

                    var watcher = new FileSystemWatcher(_workspacesPath);
                    watcher.Filter = "*.port.txt";
                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

                    FileSystemEventHandler handler = (s, e) => RequestRefresh();

                    watcher.Created += handler;
                    watcher.Changed += handler;
                    watcher.Deleted += handler;
                    watcher.Error += OnWatcherError;

                    watcher.EnableRaisingEvents = true;
                    _portFileWatcher = watcher;
                    _logCallback?.Invoke("Port file watcher initialized");

                    // Events may have been missed while the watcher was down.
                    RequestRefresh();
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error initializing port file watcher: {ex.Message}");
                    return false;
                }
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // The watcher is unreliable after an error (e.g. internal buffer overflow) -
            // tear it down and rebuild rather than silently missing events (#51).
            _logCallback?.Invoke($"Port file watcher error, restarting: {e.GetException()?.Message}");

            lock (_watcherLock)
            {
                _portFileWatcher?.Dispose();
                _portFileWatcher = null;
            }

            if (!TryInitializeWatcher())
            {
                StartRetryTimer();
            }
        }

        private void StartRetryTimer()
        {
            lock (_watcherLock)
            {
                if (_isDisposed || _retryTimer != null) return;

                _retryTimer = new System.Threading.Timer(_ =>
                {
                    if (TryInitializeWatcher())
                    {
                        StopRetryTimer();
                    }
                }, null, _retryIntervalMs, _retryIntervalMs);
            }
        }

        private void StopRetryTimer()
        {
            lock (_watcherLock)
            {
                _retryTimer?.Dispose();
                _retryTimer = null;
            }
        }

        private void RequestRefresh()
        {
            // Debounce logic
            CancellationTokenSource cts;
            lock (_watcherLock)
            {
                _refreshDebounceCts?.Cancel();
                _refreshDebounceCts?.Dispose();
                _refreshDebounceCts = new CancellationTokenSource();
                cts = _refreshDebounceCts;
            }
            var token = cts.Token;

            Task.Delay(500, token).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && !token.IsCancellationRequested)
                {
                    OnRefreshNeeded?.Invoke(this, EventArgs.Empty);
                }
            }, TaskScheduler.Default);
        }

        public void Stop()
        {
            lock (_watcherLock)
            {
                if (_portFileWatcher != null)
                {
                    _portFileWatcher.EnableRaisingEvents = false;
                }
            }
            StopRetryTimer();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            lock (_watcherLock)
            {
                _portFileWatcher?.Dispose();
                _portFileWatcher = null;
                _refreshDebounceCts?.Cancel();
                _refreshDebounceCts?.Dispose();
                _refreshDebounceCts = null;
            }
        }
    }
}
