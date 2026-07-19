using System.Net;
using System.Net.Sockets;
using System.Text;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class PowerBIDetectorTests : IDisposable
    {
        private readonly string _workspacesRoot;

        public PowerBIDetectorTests()
        {
            _workspacesRoot = Path.Combine(Path.GetTempPath(), "PBIPW.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspacesRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspacesRoot, recursive: true); } catch { }
        }

        private void WriteWorkspace(string name, int port)
        {
            var dataDir = Path.Combine(_workspacesRoot, name, "Data");
            Directory.CreateDirectory(dataDir);
            // Power BI writes the port file as UTF-16 LE
            File.WriteAllText(Path.Combine(dataDir, "msmdsrv.port.txt"), port.ToString(), Encoding.Unicode);
        }

        [Fact]
        public void IsPortAlive_TrueForListeningPort_FalseForDeadPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int alivePort = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                Assert.True(PowerBIDetector.IsPortAlive(alivePort));
            }
            finally
            {
                listener.Stop();
            }

            Assert.False(PowerBIDetector.IsPortAlive(alivePort));
        }

        [Fact]
        public void DetectRunningInstances_ListsWorkspaceWithLivePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                WriteWorkspace("AnalysisServicesWorkspace_live", port);
                var detector = new PowerBIDetector(_workspacesRoot);

                var instances = detector.DetectRunningInstances();

                var instance = Assert.Single(instances);
                Assert.Equal(port, instance.Port);
                Assert.Equal("AnalysisServicesWorkspace_live", instance.WorkspaceId);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void DetectRunningInstances_SkipsGhostWorkspaceWithDeadPort()
        {
            // Simulates a leftover workspace dir after a Desktop crash (#50):
            // valid port file, but nothing listening on the port.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            WriteWorkspace("AnalysisServicesWorkspace_ghost", deadPort);
            var detector = new PowerBIDetector(_workspacesRoot);

            var instances = detector.DetectRunningInstances();

            Assert.Empty(instances);
        }

        [Fact]
        public void DetectRunningInstances_EmptyWhenWorkspacesPathMissing()
        {
            var detector = new PowerBIDetector(Path.Combine(_workspacesRoot, "does-not-exist"));

            Assert.Empty(detector.DetectRunningInstances());
            Assert.False(detector.IsWorkspacePathValid());
        }
    }
}
