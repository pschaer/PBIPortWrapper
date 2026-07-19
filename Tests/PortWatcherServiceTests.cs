using System.Text;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class PortWatcherServiceTests : IDisposable
    {
        private readonly string _root;

        public PortWatcherServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PBIPW.Tests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void StartsWatching_WhenDirectoryExists()
        {
            Directory.CreateDirectory(_root);

            using var watcher = new PortWatcherService(null, _root);

            Assert.True(watcher.IsWatching);
        }

        [Fact]
        public async Task RecoversWhenDirectoryAppearsLater()
        {
            // #51: dir missing at construction must not leave the watcher dead forever.
            using var watcher = new PortWatcherService(null, _root, retryIntervalMs: 100);
            Assert.False(watcher.IsWatching);

            Directory.CreateDirectory(_root);

            // Allow a few retry cycles
            for (int i = 0; i < 30 && !watcher.IsWatching; i++)
                await Task.Delay(100);

            Assert.True(watcher.IsWatching);
        }

        [Fact]
        public async Task RaisesRefresh_OnPortFileCreated()
        {
            var dataDir = Path.Combine(_root, "AnalysisServicesWorkspace_x", "Data");
            Directory.CreateDirectory(dataDir);

            using var watcher = new PortWatcherService(null, _root);
            var refreshRaised = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.OnRefreshNeeded += (s, e) => refreshRaised.TrySetResult(true);

            File.WriteAllText(Path.Combine(dataDir, "msmdsrv.port.txt"), "12345", Encoding.Unicode);

            var completed = await Task.WhenAny(refreshRaised.Task, Task.Delay(5000));
            Assert.Same(refreshRaised.Task, completed);
        }
    }
}
