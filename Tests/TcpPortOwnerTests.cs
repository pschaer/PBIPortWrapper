using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the port → owning PID resolver (#94) end-to-end against a real socket:
    /// this is how the detector links a Power BI workspace to its engine now that
    /// msmdsrv's command line is no longer readable.
    /// </summary>
    public sealed class TcpPortOwnerTests
    {
        [Fact]
        public void GetOwningProcessId_finds_this_process_for_its_own_listener()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Assert.Equal(Process.GetCurrentProcess().Id, TcpPortOwner.GetOwningProcessId(port));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(70000)]
        public void GetOwningProcessId_rejects_invalid_ports(int port)
        {
            Assert.Equal(0, TcpPortOwner.GetOwningProcessId(port));
        }
    }
}
