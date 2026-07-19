using System.Net;
using System.Net.Sockets;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class AutoConnectServiceTests
    {
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static PowerBIInstance Instance(string fileName, int port) => new PowerBIInstance
        {
            WorkspaceId = $"ws-{fileName}",
            FileName = fileName,
            Port = port,
            ProcessId = 1234
        };

        private static ProxyConfiguration ConfigWith(params PortMappingRule[] rules)
        {
            var config = new ProxyConfiguration();
            config.PortMappings.AddRange(rules);
            return config;
        }

        [Fact]
        public async Task StartsProxy_ForAutoConnectRule()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);

            await service.ApplyAsync(new[] { Instance("Sales", FreePort()) }, config);

            try
            {
                Assert.True(manager.IsRunning(fixedPort));
            }
            finally
            {
                manager.StopAll();
            }
        }

        [Fact]
        public async Task SkipsRule_WithoutAutoConnect()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: false, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);

            await service.ApplyAsync(new[] { Instance("Sales", FreePort()) }, config);

            Assert.False(manager.IsRunning(fixedPort));
        }

        [Fact]
        public async Task SkipsInstance_WithoutMatchingRule()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);

            await service.ApplyAsync(new[] { Instance("Inventory", FreePort()) }, config);

            Assert.False(manager.IsRunning(fixedPort));
        }

        [Fact]
        public async Task RejectsPortsBelow1024_AndLogs()
        {
            var manager = new ProxyManager();
            var config = ConfigWith(new PortMappingRule("Sales", 80, autoConnect: true, allowNetworkAccess: false));
            string logged = null;
            var service = new AutoConnectService(manager, m => logged = m);

            await service.ApplyAsync(new[] { Instance("Sales", FreePort()) }, config);

            Assert.False(manager.IsRunning(80));
            Assert.Contains("Invalid port", logged);
        }

        [Fact]
        public async Task DoesNotRestart_AlreadyRunningProxy()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            int originalTarget = FreePort();
            await manager.StartProxyAsync(fixedPort, originalTarget, false, "Sales");

            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);

            try
            {
                await service.ApplyAsync(new[] { Instance("Sales", FreePort()) }, config);

                // Still forwarding to the original target: reconciliation (stop on
                // mismatch) is the caller's responsibility before auto-connect runs.
                Assert.Equal(originalTarget, manager.GetTargetPort(fixedPort));
            }
            finally
            {
                manager.StopAll();
            }
        }

        [Fact]
        public async Task NullConfigOrInstances_AreNoOps()
        {
            var service = new AutoConnectService(new ProxyManager());

            await service.ApplyAsync(null, new ProxyConfiguration());
            await service.ApplyAsync(Array.Empty<PowerBIInstance>(), null);
        }

        [Fact]
        public async Task SuppressedInstance_IsNotAutoStarted()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);
            var instance = Instance("Sales", FreePort());

            service.SuppressUntilExplicitStart(instance.WorkspaceId);
            await service.ApplyAsync(new[] { instance }, config);

            Assert.False(manager.IsRunning(fixedPort));
        }

        [Fact]
        public async Task ClearedSuppression_AllowsAutoStartAgain()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);
            var instance = Instance("Sales", FreePort());

            service.SuppressUntilExplicitStart(instance.WorkspaceId);
            service.ClearSuppression(instance.WorkspaceId);
            await service.ApplyAsync(new[] { instance }, config);

            try
            {
                Assert.True(manager.IsRunning(fixedPort));
            }
            finally
            {
                manager.StopAll();
            }
        }

        [Fact]
        public async Task Suppression_IsClearedWhenInstanceLeavesSnapshot()
        {
            var manager = new ProxyManager();
            int fixedPort = FreePort();
            var config = ConfigWith(new PortMappingRule("Sales", fixedPort, autoConnect: true, allowNetworkAccess: false));
            var service = new AutoConnectService(manager);
            var instance = Instance("Sales", FreePort());

            service.SuppressUntilExplicitStart(instance.WorkspaceId);

            // Instance disappears (Desktop closed) -> suppression is dropped...
            await service.ApplyAsync(Array.Empty<PowerBIInstance>(), config);
            Assert.False(service.IsSuppressed(instance.WorkspaceId));

            // ...so a fresh session of the same model auto-starts again.
            await service.ApplyAsync(new[] { instance }, config);

            try
            {
                Assert.True(manager.IsRunning(fixedPort));
            }
            finally
            {
                manager.StopAll();
            }
        }

        [Fact]
        public void Suppression_IgnoresNullOrEmptyWorkspaceId()
        {
            var service = new AutoConnectService(new ProxyManager());

            service.SuppressUntilExplicitStart(null);
            service.SuppressUntilExplicitStart("");
            service.ClearSuppression(null);

            Assert.False(service.IsSuppressed(null));
            Assert.False(service.IsSuppressed(""));
        }
    }
}
