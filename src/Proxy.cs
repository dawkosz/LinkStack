using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinkStack {
    public sealed class ProxyEngine : IDisposable {
        public const int HttpPort = 17881, SocksPort = 17880;
        public readonly NetworkDialer Dialer;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
        readonly SemaphoreSlim slots = new SemaphoreSlim(256);
        TcpListener http, socks;
        public bool Running { get { return http != null && !stop.IsCancellationRequested; } }
        public ProxyEngine(Link[] links, Action<string> log) { Dialer = new NetworkDialer(links, log); }
        public void Start() {
            try {
                http = new TcpListener(IPAddress.Loopback, HttpPort); http.Start();
                socks = new TcpListener(IPAddress.Loopback, SocksPort); socks.Start();
                Accept(http, false); Accept(socks, true);
            } catch { Dispose(); throw; }
        }
        async void Accept(TcpListener listener, bool isSocks) {
            while (!stop.IsCancellationRequested) {
                TcpClient c = null;
                try {
                    c = await listener.AcceptTcpClientAsync();
                    if (!slots.Wait(0)) { c.Close(); continue; }
                    clients.TryAdd(c, 0); Handle(c, isSocks);
                } catch { if (c != null) c.Close(); if (stop.IsCancellationRequested) break; }
            }
        }
        async void Handle(TcpClient c, bool isSocks) {
            try {
                c.NoDelay = true;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                    handshake.CancelAfter(15000);
                    if (isSocks) await Socks(c, handshake.Token); else await Http(c, handshake.Token);
                }
            } catch (Exception e) {
                if (!stop.IsCancellationRequested && !(e is OperationCanceledException) && !IsPeerClosed(e) && Dialer.Log != null) Dialer.Log("Połączenie: " + e.Message);
            } finally { byte b; clients.TryRemove(c, out b); c.Close(); slots.Release(); }
        }
        static async Task<byte[]> Read(Stream s, int count, CancellationToken token) {
            byte[] b = new byte[count]; int at = 0;
            while (at < count) { int n = await s.ReadAsync(b, at, count - at, token); if (n == 0) throw new EndOfStreamException(); at += n; }
            return b;
        }
        static async Task Write(Stream s, byte[] b, CancellationToken t) { await s.WriteAsync(b, 0, b.Length, t); }
        static Task Text(Stream s, string value, CancellationToken t) { return Write(s, Encoding.ASCII.GetBytes(value), t); }
        async Task Socks(TcpClient c, CancellationToken t) {
            var s = c.GetStream(); byte[] greeting = await Read(s, 2, t);
            if (greeting[0] != 5 || greeting[1] == 0) return;
            byte[] methods = await Read(s, greeting[1], t);
            if (!methods.Contains((byte)0)) { await Write(s, new byte[] {5, 255}, t); return; }
            await Write(s, new byte[] {5, 0}, t);
            byte[] h = await Read(s, 4, t);
            if (h[0] != 5 || h[2] != 0 || h[1] != 1) { await SocksError(s, 7, t); return; }
            string host;
            if (h[3] == 1) host = new IPAddress(await Read(s, 4, t)).ToString();
            else if (h[3] == 3) { byte[] l = await Read(s, 1, t); if (l[0] == 0) return; host = Encoding.ASCII.GetString(await Read(s, l[0], t)); }
            else { await SocksError(s, 8, t); return; }
            byte[] p = await Read(s, 2, t); int port = (p[0] << 8) | p[1];
            Dialed d = null;
            try { d = await Dialer.Connect(host, port, t, null); }
            catch {}
            if (d == null) { await SocksError(s, 5, t); return; }
            using (d) {
                var ep = (IPEndPoint)d.Socket.LocalEndPoint;
                byte[] reply = new byte[] {5, 0, 0, 1, 0, 0, 0, 0, (byte)(ep.Port >> 8), (byte)ep.Port};
                Array.Copy(ep.Address.GetAddressBytes(), 0, reply, 4, 4);
                await Write(s, reply, t);
                await Relay(c, d);
            }
        }
        static Task SocksError(Stream s, byte code, CancellationToken t) { return Write(s, new byte[] {5, code, 0, 1, 0, 0, 0, 0, 0, 0}, t); }
        async Task Http(TcpClient c, CancellationToken t) {
            var s = c.GetStream(); var header = new MemoryStream(); int end = 0;
            while (header.Length < 32768) {
                byte[] b = await Read(s, 1, t); header.WriteByte(b[0]);
                end = ((end << 8) | b[0]); if (end == 0x0D0A0D0A) break;
            }
            if (end != 0x0D0A0D0A) { await Text(s, "HTTP/1.1 431 Request Header Fields Too Large\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", t); return; }
            string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] {"\r\n"}, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length != 3) { await HttpError(s, 400, t); return; }
            bool connect = first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
            Uri uri;
            if (!Uri.TryCreate(connect ? "http://" + first[1] : first[1], UriKind.Absolute, out uri) ||
                uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) { await HttpError(s, 400, t); return; }
            if (connect && (uri.AbsolutePath != "/" || first[1].IndexOf(':') < 0)) { await HttpError(s, 400, t); return; }
            Dialed d = null;
            try { d = await Dialer.Connect(uri.Host, uri.Port, t, null); }
            catch {}
            if (d == null) { await HttpError(s, 502, t); return; }
            using (d) {
                if (connect) await Text(s, "HTTP/1.1 200 Connection Established\r\n\r\n", t);
                else {
                    // One origin per client connection; disable keep-alive to avoid cross-origin reuse.
                    var outHeader = new StringBuilder(first[0] + " " + uri.PathAndQuery + " " + first[2] + "\r\n");
                    var hop = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) {"Proxy-Connection", "Proxy-Authorization", "Connection", "Keep-Alive", "Host"};
                    foreach (string line in lines.Skip(1)) if (line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) foreach (string value in line.Substring(11).Split(',')) hop.Add(value.Trim());
                    foreach (string line in lines.Skip(1)) {
                        int colon = line.IndexOf(':');
                        if (line.Length == 0) continue;
                        if (colon < 1) { await HttpError(s, 400, t); return; }
                        if (!hop.Contains(line.Substring(0, colon))) outHeader.Append(line).Append("\r\n");
                    }
                    outHeader.Append("Host: ").Append(uri.Authority).Append("\r\nConnection: close\r\n\r\n");
                    using (var ns = new NetworkStream(d.Socket, false)) await Text(ns, outHeader.ToString(), t);
                }
                await Relay(c, d);
            }
        }
        static Task HttpError(Stream s, int code, CancellationToken t) { return Text(s, "HTTP/1.1 " + code + " Proxy Error\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", t); }
        internal static bool IsPeerClosed(Exception e) {
            var socket = e as SocketException;
            if (socket != null) return socket.SocketErrorCode == SocketError.ConnectionReset || socket.SocketErrorCode == SocketError.ConnectionAborted || socket.SocketErrorCode == SocketError.OperationAborted || socket.SocketErrorCode == SocketError.Shutdown;
            return e.InnerException != null && IsPeerClosed(e.InnerException);
        }
        public async Task Relay(TcpClient client, Dialed remote) {
            Interlocked.Increment(ref remote.Link.Active);
            try {
                using (var ns = new NetworkStream(remote.Socket, false)) {
                    Task up = Pump(client.GetStream(), ns, remote.Link, false, remote.Socket);
                    Task down = Pump(ns, client.GetStream(), remote.Link, true, client.Client);
                    Task done = await Task.WhenAny(up, down);
                    if (done.IsFaulted || done.IsCanceled) { client.Close(); remote.Dispose(); }
                    await Task.WhenAll(up, down);
                }
            } finally { Interlocked.Decrement(ref remote.Link.Active); }
        }
        async Task Pump(Stream input, Stream output, Link link, bool download, Socket destination) {
            byte[] b = new byte[65536];
            while (true) {
                int n = await input.ReadAsync(b, 0, b.Length, stop.Token);
                if (n == 0) { try { destination.Shutdown(SocketShutdown.Send); } catch {} return; }
                await output.WriteAsync(b, 0, n, stop.Token);
                if (download) Interlocked.Add(ref link.Received, n); else Interlocked.Add(ref link.Sent, n);
            }
        }
        public void Dispose() {
            stop.Cancel();
            if (http != null) http.Stop(); if (socks != null) socks.Stop();
            foreach (var c in clients.Keys) c.Close();
        }
    }
}
