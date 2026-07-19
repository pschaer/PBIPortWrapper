using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Owns Power BI instance detection end to end: file watcher, periodic polling,
    /// and serialized scans. Consumers read <see cref="CurrentInstances"/> or react
    /// to <see cref="InstancesChanged"/> — detection state no longer lives in the UI.
    ///
    /// Refresh requests arriving while a scan runs are coalesced into one follow-up
    /// scan instead of being dropped, so a burst of file events cannot cause a
    /// missed final state.
    ///
    /// <see cref="InstancesChanged"/> is raised on a thread-pool thread; UI
    /// consumers must marshal to their own thread.
    /// </summary>
    public class InstanceMonitor : IDisposable
    {
        private readonly PowerBIDetector _detector;
        private readonly PortWatcherService _watcher;
        private readonly System.Threading.Timer _pollTimer;
        private readonly Action<string> _logCallback;
        private readonly object _scanLock = new object();
        private bool _scanRunning;
        private bool _scanPending;
        private bool _isDisposed;
        private volatile IReadOnlyList<PowerBIInstance> _current = Array.Empty<PowerBIInstance>();

        public IReadOnlyList<PowerBIInstance> CurrentInstances => _current;

        public event EventHandler<InstancesChangedEventArgs> InstancesChanged;

        public InstanceMonitor(
            PowerBIDetector detector = null,
            Action<string> logCallback = null,
            string workspacesPath = null,
            int pollIntervalMs = 10000)
        {
            _detector = detector ?? new PowerBIDetector(workspacesPath);
            _logCallback = logCallback;

            _watcher = new PortWatcherService(logCallback, workspacesPath);
            _watcher.OnRefreshNeeded += (s, e) => RequestRefresh();

            _pollTimer = new System.Threading.Timer(_ => RequestRefresh(), null, pollIntervalMs, pollIntervalMs);
        }

        /// <summary>
        /// Triggers a scan. Never blocks; if a scan is already running, exactly one
        /// follow-up scan is queued.
        /// </summary>
        public void RequestRefresh()
        {
            lock (_scanLock)
            {
                if (_isDisposed) return;
                if (_scanRunning)
                {
                    _scanPending = true;
                    return;
                }
                _scanRunning = true;
            }

            _ = Task.Run(ScanLoop);
        }

        private void ScanLoop()
        {
            while (true)
            {
                IReadOnlyList<PowerBIInstance> snapshot = null;
                try
                {
                    snapshot = _detector.DetectRunningInstances();
                }
                catch (Exception ex)
                {
                    _logCallback?.Invoke($"Instance scan failed: {ex.Message}");
                }

                if (snapshot != null)
                {
                    _current = snapshot;
                    try
                    {
                        InstancesChanged?.Invoke(this, new InstancesChangedEventArgs(snapshot));
                    }
                    catch (Exception ex)
                    {
                        _logCallback?.Invoke($"Error in InstancesChanged handler: {ex.Message}");
                    }
                }

                lock (_scanLock)
                {
                    if (_scanPending && !_isDisposed)
                    {
                        _scanPending = false;
                        continue;
                    }
                    _scanRunning = false;
                    return;
                }
            }
        }

        public void Dispose()
        {
            lock (_scanLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _scanPending = false;
            }

            _pollTimer.Dispose();
            _watcher.Dispose();
        }
    }

    public class InstancesChangedEventArgs : EventArgs
    {
        public IReadOnlyList<PowerBIInstance> Instances { get; }

        public InstancesChangedEventArgs(IReadOnlyList<PowerBIInstance> instances)
        {
            Instances = instances;
        }
    }
}
