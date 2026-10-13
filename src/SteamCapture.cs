using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace LinkStack {
    // Transparent redirect for new Steam IPv4 TCP connections on ports 80/443.
    // Packet metadata layout follows WinDivert 2.2; no persistent routes are installed.
    public sealed class SteamCapture : IDisposable {
        public const int Port = 17882;
        IntPtr handle = new IntPtr(-1);
        TcpListener listener;
        readonly ProxyEngine engine;
        readonly int testProcessId;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly ConcurrentDictionary<string, Flow> flows = new ConcurrentDictionary<string, Flow>();
        readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
        readonly SemaphoreSlim slots = new SemaphoreSlim(128);
        public long Redirected, Packets;
        Task packetTask;
        public bool Running { get { return handle != new IntPtr(-1) && !stop.IsCancellationRequested; } }
        sealed class Flow { public int Port; public long Seen; }
        public SteamCapture(ProxyEngine engine) { this.engine = engine; }
        internal SteamCapture(ProxyEngine engine, int testProcessId) { this.engine = engine; this.testProcessId = testProcessId; }
        public static bool IsAdmin { get { using (var i = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(i).IsInRole(WindowsBuiltInRole.Administrator); } }
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        static extern IntPtr WinDivertOpen([MarshalAs(UnmanagedType.LPStr)] string filter, int layer, short priority, ulong flags);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool WinDivertRecv(IntPtr handle, [Out] byte[] packet, uint len, out uint read, [Out] byte[] address);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint len, out uint sent, byte[] address);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool WinDivertHelperCalcChecksums(byte[] packet, uint len, byte[] address, ulong flags);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool WinDivertClose(IntPtr handle);
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        public void Start() {
            if (!IsAdmin) throw new InvalidOperationException("Tryb Steam wymaga uruchomienia LinkStack jako administrator.");
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinDivert.dll"))) throw new FileNotFoundException("Brak WinDivert.dll obok LinkStack.exe.");
            try {
                listener = new TcpListener(IPAddress.Any, Port); listener.Start();
                handle = WinDivertOpen("outbound and ip and tcp and !loopback and (tcp.DstPort == 80 or tcp.DstPort == 443 or tcp.SrcPort == 17882)", 0, 125, 0);
                if (handle == new IntPtr(-1)) throw new InvalidOperationException("WinDivert: błąd " + Marshal.GetLastWin32Error() + ". Sprawdź uprawnienia administratora i sterownik.");
                packetTask = Task.Factory.StartNew(PacketLoop, TaskCreationOptions.LongRunning);
                Accept();
            } catch { Dispose(); throw; }
        }
        static int ReadPort(byte[] p, int at) { return p[at] * 256 + p[at + 1]; }
        static void WritePort(byte[] p, int at, int value) { p[at] = (byte)(value >> 8); p[at + 1] = (byte)value; }
        static string Key(uint local, uint remote, int clientPort) { return local + ":" + remote + ":" + clientPort; }
        static uint Ip(IPAddress ip) { return BitConverter.ToUInt32(ip.GetAddressBytes(), 0); }
        static void SwapAddresses(byte[] p) { for (int i = 0; i < 4; i++) { byte b = p[12 + i]; p[12 + i] = p[16 + i]; p[16 + i] = b; } }
        void PacketLoop() {
            byte[] packet = new byte[65535], address = new byte[80]; uint length, sent;
            long lastSweep = 0;
            while (!stop.IsCancellationRequested) {
                if (!WinDivertRecv(handle, packet, (uint)packet.Length, out length, address)) {
                    if (!stop.IsCancellationRequested && engine.Dialer.Log != null) engine.Dialer.Log("Odczyt WinDivert przerwany: " + Marshal.GetLastWin32Error());
                    stop.Cancel(); break;
                }
                bool changed = false;
                try {
                    if (length >= 40 && (packet[0] >> 4) == 4 && packet[9] == 6 && (ReadPort(packet, 6) & 0x3fff) == 0) {
                        int ihl = (packet[0] & 15) * 4;
                        if (ihl >= 20 && length >= ihl + 20) {
                            uint source = BitConverter.ToUInt32(packet, 12), destination = BitConverter.ToUInt32(packet, 16);
                            int sp = ReadPort(packet, ihl), dp = ReadPort(packet, ihl + 2);
                            Flow flow; long now = DateTime.UtcNow.Ticks;
                            if (sp == Port) {
                                if (flows.TryGetValue(Key(source, destination, dp), out flow)) {
                                    WritePort(packet, ihl, flow.Port); SwapAddresses(packet); address[10] = (byte)(address[10] & ~2); flow.Seen = now; changed = true;
                                }
                            } else {
                                string key = Key(source, destination, sp);
                                bool syn = (packet[ihl + 13] & 0x12) == 0x02;
                                if (syn) {
                                    if (IsSteamConnection(source, sp, destination, dp)) {
                                        if (!flows.TryGetValue(key, out flow) || flow.Port != dp) {
                                            flow = new Flow { Port = dp, Seen = now }; flows[key] = flow;
                                            Interlocked.Increment(ref Redirected);
                                        }
                                    } else { flows.TryRemove(key, out flow); }
                                }
                                if (flows.TryGetValue(key, out flow) && flow.Port == dp) {
                                    WritePort(packet, ihl + 2, Port); SwapAddresses(packet); address[10] = (byte)(address[10] & ~2); flow.Seen = now; changed = true;
                                }
                            }
                            if (now - lastSweep > TimeSpan.FromMinutes(1).Ticks) {
                                foreach (var pair in flows) if (now - pair.Value.Seen > TimeSpan.FromMinutes(10).Ticks) { Flow unused; flows.TryRemove(pair.Key, out unused); }
                                lastSweep = now;
                            }
                        }
                    }
                } catch (Exception e) { if (engine.Dialer.Log != null) engine.Dialer.Log("Przechwytywanie: " + e.Message); }
                if (changed) { WinDivertHelperCalcChecksums(packet, length, address, 0); Interlocked.Increment(ref Packets); }
                if (!WinDivertSend(handle, packet, length, out sent, address) && !stop.IsCancellationRequested && engine.Dialer.Log != null) engine.Dialer.Log("Wysłanie pakietu: " + Marshal.GetLastWin32Error());
            }
        }
        bool IsSteamConnection(uint local, int localPort, uint remote, int remotePort) {
            int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
            if (size < 4 || size > 16 * 1024 * 1024) return false;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                if (GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0) != 0) return false;
                int rows = Marshal.ReadInt32(buffer);
                if (rows < 0 || rows > (size - 4) / 24) return false;
                for (int i = 0; i < rows; i++) {
                    int at = 4 + i * 24;
                    int lp = Marshal.ReadByte(buffer, at + 8) * 256 + Marshal.ReadByte(buffer, at + 9);
                    int rp = Marshal.ReadByte(buffer, at + 16) * 256 + Marshal.ReadByte(buffer, at + 17);
                    if (lp != localPort || rp != remotePort || unchecked((uint)Marshal.ReadInt32(buffer, at + 4)) != local || unchecked((uint)Marshal.ReadInt32(buffer, at + 12)) != remote) continue;
                    try {
                        int pid = Marshal.ReadInt32(buffer, at + 20);
                        if (testProcessId != 0) return pid == testProcessId;
                        string name = Process.GetProcessById(pid).ProcessName;
                        return name.Equals("steam", StringComparison.OrdinalIgnoreCase) || name.Equals("steamservice", StringComparison.OrdinalIgnoreCase) || name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase);
                    } catch { return false; }
                }
            } finally { Marshal.FreeHGlobal(buffer); }
            return false;
        }
        async void Accept() {
            while (!stop.IsCancellationRequested) {
                try {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    if (!slots.Wait(0)) { client.Close(); continue; }
                    clients.TryAdd(client, 0); Serve(client);
                } catch { if (stop.IsCancellationRequested) break; }
            }
        }
        async void Serve(TcpClient c) {
            try {
                var local = (IPEndPoint)c.Client.LocalEndPoint; var remote = (IPEndPoint)c.Client.RemoteEndPoint; Flow flow;
                if (!flows.TryGetValue(Key(Ip(local.Address), Ip(remote.Address), remote.Port), out flow)) throw new InvalidOperationException("Brak mapowania Steam.");
                using (var d = await engine.Dialer.Connect(remote.Address.ToString(), flow.Port, stop.Token, null)) await engine.Relay(c, d);
            } catch (Exception e) { if (!stop.IsCancellationRequested && !ProxyEngine.IsPeerClosed(e) && engine.Dialer.Log != null) engine.Dialer.Log("Steam: " + e.Message); }
            finally { byte b; clients.TryRemove(c, out b); c.Close(); slots.Release(); }
        }
        public void Dispose() {
            stop.Cancel();
            if (listener != null) listener.Stop();
            if (handle != new IntPtr(-1)) { WinDivertClose(handle); handle = new IntPtr(-1); }
            foreach (var c in clients.Keys) c.Close();
            if (packetTask != null) try { packetTask.Wait(2000); } catch {}
        }
    }
}
