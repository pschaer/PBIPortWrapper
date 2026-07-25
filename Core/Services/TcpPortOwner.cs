using System;
using System.Runtime.InteropServices;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Maps a local TCP listening port to the process that owns it, via the IP Helper
    /// API (GetExtendedTcpTable). When a Power BI Desktop engine runs elevated (e.g.
    /// Desktop launched from an elevated context), a non-elevated wrapper can't read
    /// its command line to match it to a workspace by path (#94). Matching by the
    /// workspace's AS port works regardless of elevation; this resolves that port to
    /// the owning msmdsrv PID.
    /// </summary>
    public static class TcpPortOwner
    {
        private const int AF_INET = 2;                       // IPv4
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;  // listening sockets + owning PID
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>The PID listening on <paramref name="port"/> (IPv4), or 0 if none/unknown.</summary>
        public static int GetOwningProcessId(int port)
        {
            if (port <= 0 || port > 65535) return 0;

            int size = 0;
            uint sizing = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (sizing != ERROR_INSUFFICIENT_BUFFER && sizing != 0) return 0;
            if (size <= 0) return 0;

            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(table, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0)
                    return 0;

                int count = Marshal.ReadInt32(table);
                IntPtr rowPtr = IntPtr.Add(table, 4);
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    // Port is in the low 16 bits, network byte order.
                    int localPort = ((int)(row.localPort & 0xFF) << 8) | (int)((row.localPort >> 8) & 0xFF);
                    if (localPort == port) return (int)row.owningPid;
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }

            return 0;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }
    }
}
