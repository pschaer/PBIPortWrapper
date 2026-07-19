using System.Net;
using System.Net.Sockets;
using System.Text;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class InstanceMonitorTests : IDisposable
    {
        private readonly string _root;
        private readonly List<IDisposable> _cleanup = new();

        public InstanceMonitorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PBIPW.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            foreach (var d in _cleanup) { try { d.Dispose(); } catch { } }
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private TcpListener StartListener()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            // Close accepted connections immediately so the detector's ADOMD probe
            // fails fast instead of waiting out its protocol timeout.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        client.Close();
                    }
                }
                catch { /* listener stopped */ }
            });
            _cleanup.Add(new DisposableAction(listener.Stop));
            return listener;
        }

        private void WriteWorkspace(string name, int port)
        {
            var dataDir = Path.Combine(_root, name, "Data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "msmdsrv.port.txt"), port.ToString(), Encoding.Unicode);
        }

        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _action;
            public DisposableAction(Action action) => _action = action;
            public void Dispose() => _action();
        }

        [Fact]
        public async Task RequestRefresh_RaisesInstancesChanged_WithSnapshot()
        {
            var listener = StartListener();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            WriteWorkspace("AnalysisServicesWorkspace_a", port);

            using var monitor = new InstanceMonitor(
                new PowerBIDetector(_root), workspacesPath: _root, pollIntervalMs: 60000);

            var changed = new TaskCompletionSource<IReadOnlyList<Models.PowerBIInstance>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.InstancesChanged += (s, e) => changed.TrySetResult(e.Instances);

            monitor.RequestRefresh();

            var completed = await Task.WhenAny(changed.Task, Task.Delay(10000));
            Assert.Same(changed.Task, completed);
            var instance = Assert.Single(changed.Task.Result);
            Assert.Equal(port, instance.Port);
            Assert.Equal(changed.Task.Result, monitor.CurrentInstances);
        }

        [Fact]
        public async Task FileEvent_TriggersScan_WithoutExplicitRequest()
        {
            var listener = StartListener();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            using var monitor = new InstanceMonitor(
                new PowerBIDetector(_root), workspacesPath: _root, pollIntervalMs: 60000);

            var found = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.InstancesChanged += (s, e) =>
            {
                if (e.Instances.Count == 1) found.TrySetResult(true);
            };

            // Simulates Desktop starting up: workspace dir + port file appear.
            WriteWorkspace("AnalysisServicesWorkspace_b", port);

            var completed = await Task.WhenAny(found.Task, Task.Delay(10000));
            Assert.Same(found.Task, completed);
        }

        [Fact]
        public async Task ConcurrentRequests_AreCoalesced_NotDropped()
        {
            using var monitor = new InstanceMonitor(
                new PowerBIDetector(_root), workspacesPath: _root, pollIntervalMs: 60000);

            int scans = 0;
            monitor.InstancesChanged += (s, e) => Interlocked.Increment(ref scans);

            for (int i = 0; i < 20; i++) monitor.RequestRefresh();

            // Let all queued work drain
            await Task.Delay(2000);

            // Burst of 20 requests must produce at least one scan (none dropped into
            // the void) but far fewer than 20 (coalescing works).
            Assert.InRange(Volatile.Read(ref scans), 1, 5);
        }

        [Fact]
        public void Dispose_IsSafe_DuringActivity()
        {
            var monitor = new InstanceMonitor(
                new PowerBIDetector(_root), workspacesPath: _root, pollIntervalMs: 50);

            for (int i = 0; i < 5; i++) monitor.RequestRefresh();
            monitor.Dispose();

            // No further scans may start after dispose
            monitor.RequestRefresh();
        }
    }
}
