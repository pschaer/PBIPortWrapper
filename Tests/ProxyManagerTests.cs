using System.Net;
using System.Net.Sockets;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class ProxyManagerTests
    {
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task GetTargetPort_ReflectsRunningProxy_AndNullAfterStop()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            int targetPort = FreePort();

            Assert.Null(manager.GetTargetPort(fixedPort));

            await manager.StartProxyAsync(fixedPort, targetPort, allowNetworkAccess: false, "Test");
            try
            {
                Assert.True(manager.IsRunning(fixedPort));
                Assert.Equal(targetPort, manager.GetTargetPort(fixedPort));
            }
            finally
            {
                manager.StopProxy(fixedPort);
            }

            Assert.False(manager.IsRunning(fixedPort));
            Assert.Null(manager.GetTargetPort(fixedPort));
        }

        [Fact]
        public async Task StaleTargetScenario_DetectableViaGetTargetPort()
        {
            // The #49 reconciliation contract: a running proxy whose TargetPort differs
            // from the instance's current port must be identified as stale.
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            int oldWorkspacePort = FreePort();
            int newWorkspacePort = FreePort();

            await manager.StartProxyAsync(fixedPort, oldWorkspacePort, false, "Sample01");
            try
            {
                int? current = manager.GetTargetPort(fixedPort);
                Assert.True(current.HasValue && current.Value != newWorkspacePort,
                    "stale target must be detectable by comparing GetTargetPort with the instance port");
            }
            finally
            {
                manager.StopProxy(fixedPort);
            }
        }
    }
}
