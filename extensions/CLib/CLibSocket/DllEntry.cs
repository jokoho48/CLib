using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CLibSocket {
    // ReSharper disable once UnusedMember.Global
    public class DllEntry {
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID {
            public uint LocalAddress;
            private ushort localPort;
            public uint PID;

            public ushort LocalPort {
                get {
                    var bytes = BitConverter.GetBytes(this.localPort);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(bytes);

                    return BitConverter.ToUInt16(bytes, 0);
                }
            }
        }

        // ReSharper disable once InconsistentNaming
        private enum UDP_TABLE_CLASS {
            // ReSharper disable InconsistentNaming
            // ReSharper disable UnusedMember.Local
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID,
            UDP_TABLE_OWNER_MODULE
            // ReSharper enable InconsistentNaming
            // ReSharper enable UnusedMember.Local
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, UDP_TABLE_CLASS tblClass, uint reserved = 0);

        private class TcpClientEntry {
            public Uri Uri;
            public TcpClient TcpClient;
        }

        private static readonly Dictionary<string, TcpClientEntry> TcpClients = new Dictionary<string, TcpClientEntry>();
        private static readonly ReaderWriterLock Locker = new ReaderWriterLock();
        private static List<int> _serverPorts = new List<int>();

        static DllEntry() {
            System.IO.File.WriteAllText("CLibSocket.log", string.Empty);

            new Timer(OnTick, null, 0, 5000);
        }

#if WIN64
        [DllExport("RVExtensionVersion")]
#else
        [DllExport("_RVExtensionVersion@8", CallingConvention.StdCall)]
#endif
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RVExtensionVersion(StringBuilder output, int outputSize) {
#pragma warning restore IDE0060 // Remove unused parameter
            output.Append(GetVersion());
        }

#if WIN64
        [DllExport("RVExtension")]
#else
        [DllExport("_RVExtension@12", CallingConvention.StdCall)]
#endif
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RVExtension(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string input) {
#pragma warning restore IDE0060 // Remove unused parameter
            if (input != "version")
                return;

            output.Append(GetVersion());
        }

        private static string GetVersion() {
            var executingAssembly = Assembly.GetExecutingAssembly();
            try {
                var location = executingAssembly.Location;
                return FileVersionInfo.GetVersionInfo(location).FileVersion;
            } catch (Exception) {
                // ignored
            }

            return "0.0.0.0";
        }

        [DllExport("Connect")]
        // ReSharper disable once UnusedMember.Global
        public static string Connect(string address) {
            string hash;
            using (var sha1 = new SHA1Managed()) {
                hash = string.Join("", sha1.ComputeHash(Encoding.Default.GetBytes(address)).Select(b => b.ToString("x2"))).Substring(0, 7);
            }
            Log(hash);

            if (TcpClients.ContainsKey(hash)) {
                return hash;
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) {
                Log("Malformed address: " + address);
                return "error";
            }

            Log("Connecting to: " + uri.Host + ":" + uri.Port);
            var tcpClient = Connect(uri);
            TcpClients.Add(hash, new TcpClientEntry { Uri = uri, TcpClient = tcpClient });

            return hash;
        }

        [DllExport("Disconnect")]
        // ReSharper disable once UnusedMember.Global
        public static string Disconnect(string hash) {
            if (!TcpClients.ContainsKey(hash)) {
                return "false";
            }

            TcpClients[hash].TcpClient.Close();
            TcpClients.Remove(hash);

            return "success";
        }

        [DllExport("IsConnected")]
        // ReSharper disable once UnusedMember.Global
        public static string IsConnected(string hash) {
            if (!TcpClients.ContainsKey(hash)) {
                return "error";
            }

            return IsConnected(TcpClients[hash].TcpClient) ? "success" : "false";
        }

        [DllExport("Send")]
        // ReSharper disable once UnusedMember.Global
        public static string Send(string data) {
            var dataParts = data.Split(new[] { ':' }, 2);
            var hash = dataParts[0];
            data = dataParts[1];

            if (!TcpClients.ContainsKey(hash))
                return "Socket for address not connected";

            Send(TcpClients[hash].TcpClient, data);

            return "success";
        }

        private static TcpClient Connect(Uri uri) {
            var tcpClient = new TcpClient(uri.Host, uri.Port);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            return tcpClient;
        }

        private static bool IsConnected(TcpClient tcpClient) {
            try {
                return tcpClient.Connected && !(tcpClient.Client.Poll(1, SelectMode.SelectRead) && tcpClient.Client.Available == 0);
            } catch (ObjectDisposedException) {
                return false;
            }
        }

        private static void Send(TcpClient tcpClient, string message) {
            var dataBytes = Encoding.Default.GetBytes(message);
            tcpClient.GetStream().Write(dataBytes, 0, dataBytes.Length);
        }

        private static void OnTick(object state) {
            ReconnectAllSockets();

            var ports = GetArmaServerPorts();
            if (ports.SequenceEqual(_serverPorts))
                return;

            _serverPorts = ports;
            foreach (var tcpClientEntry in TcpClients.Values) {
                Send(tcpClientEntry.TcpClient, $"PORTS:{string.Join(":", _serverPorts)}");
            }
        }

        private static void ReconnectAllSockets() {
            foreach (var kvPair in TcpClients) {
                var entry = kvPair.Value;

                if (IsConnected(entry.TcpClient))
                    continue;

                try {
                    entry.TcpClient.Close();
                } catch (ObjectDisposedException) { }

                try {
                    entry.TcpClient = Connect(entry.Uri);
                    Log("Reconnected");
                } catch (SocketException e) {
                    Log($"Reconnect - Socket exception {e.Message}");
                }
            }
        }

        private static List<int> GetArmaServerPorts() {
            var ports = new List<int>();
            var bufferSize = 0;
            var bufferTable = Marshal.AllocHGlobal(bufferSize);

            try {
                var ret = GetExtendedUdpTable(bufferTable, ref bufferSize, false, (int)AddressFamily.InterNetwork, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
                if (ret != 0) {
                    return ports;
                }

                var numberOfEntries = Marshal.ReadInt32(bufferTable, 0);
                var rowPtr = bufferTable + 4;

                for (var i = 0; i < numberOfEntries; i++) {
                    var row = (MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_UDPROW_OWNER_PID));
                    rowPtr += Marshal.SizeOf(row);

                    if (row.PID == Process.GetCurrentProcess().Id && row.LocalAddress == 0)
                        ports.Add(row.LocalPort);
                }
            } finally {
                Marshal.FreeHGlobal(bufferTable);
            }

            return ports;
        }

        private static void Log(params object[] obj) {
            try {
                Locker.AcquireWriterLock(int.MaxValue);
                System.IO.File.AppendAllText("CLibSocket.log", string.Concat(DateTime.Now.ToString("HH:mm:ss.fff"), " ", string.Concat(obj), "\r\n"));
            } finally {
                Locker.ReleaseWriterLock();
            }
        }
    }
}
