using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LinkStack {
    public static class Tests {
        static int passed;
        static string Temp;
        static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
        static byte[] Payload = Enumerable.Range(0, 9 * 1024 * 1024 + 17).Select(i => (byte)((i * 37 + i / 97) % 251)).ToArray();
        [STAThread] public static int Main(string[] args) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length == 1 && args[0] == "--create-icon") { using (var icon = Brand.CreateIcon()) using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LinkStack.ico"))) icon.Save(output); return 0; }
            if (args.Contains("--live")) Console.SetOut(TextWriter.Synchronized(new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "live-test.log"), false) {AutoFlush = true}));
            if (args.Length == 2 && args[0] == "--watch-proxy") { SystemProxy.Watch(int.Parse(args[1])); return 0; }
            if (args.Length == 2 && (args[0] == "--proxy-owner" || args[0] == "--orphan-owner")) {
                try {
                    SystemProxy.Enable();
                    if (args[0] == "--orphan-owner") File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkStack", "proxy-backup.xml"));
                    File.WriteAllText(args[1], "ready"); Thread.Sleep(10000); SystemProxy.Recover(true); return 0;
                }
                catch (Exception e) { File.WriteAllText(args[1], e.ToString()); return 1; }
            }
            if (args.Length == 1 && args[0] == "--ui-check") {
                Application.EnableVisualStyles();
                using (var form = new MainForm(false)) { form.Show(); Application.DoEvents(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LinkStack-preview.png")); } form.Close(); }
                Console.WriteLine("UI preview saved"); return 0;
            }
            if (args.Length == 3 && args[0] == "--capture-probe") {
                try {
                    for (int i = 0; i < 100 && !File.Exists(args[1]); i++) Thread.Sleep(100);
                    if (!File.Exists(args[1])) throw new TimeoutException("Brak sygnału start.");
                    var req = (HttpWebRequest)WebRequest.Create("https://api.ipify.org/"); req.Proxy = null; req.KeepAlive = false; req.Timeout = 15000;
                    using (var res = req.GetResponse()) using (var reader = new StreamReader(res.GetResponseStream())) File.WriteAllText(args[2], reader.ReadToEnd());
                    return 0;
                } catch (Exception e) { File.WriteAllText(args[2], e.ToString()); return 1; }
            }
            Temp = Path.Combine(Path.GetTempPath(), "LinkStack-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Temp);
            try {
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                if (args.Contains("--live")) Live(args.Contains("--capture")).GetAwaiter().GetResult();
                else if (args.Contains("--proxy-recovery")) ProxyRecovery();
                else Run().GetAwaiter().GetResult();
                Console.WriteLine("ALL PASSED: " + passed); return 0;
            }
            catch (Exception e) { Console.WriteLine(e); return 1; }
            finally { try { Directory.Delete(Temp, true); } catch {} }
        }
        static string ProxySnapshot() {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) return string.Join("\n", new[] {"ProxyEnable", "ProxyServer", "ProxyOverride"}.Select(n => n + "=" + (k.GetValue(n) == null ? "<missing>" : k.GetValueKind(n) + ":" + k.GetValue(n)))) + "\nWinINet=" + SystemProxy.NativeStateForTests();
        }
        static void ProxyRecovery() {
            string before = ProxySnapshot(), nativeBefore = SystemProxy.NativeStateForTests(); string ready = Path.Combine(Temp, "ready");
            using (var p = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--proxy-owner \"" + ready + "\"") {UseShellExecute = false})) {
                for (int i = 0; i < 100 && !File.Exists(ready); i++) Thread.Sleep(50);
                Check(File.Exists(ready) && File.ReadAllText(ready) == "ready", "proxy snapshot and crash watcher started");
                try { Check(SystemProxy.IsActive, "local proxy is active in native WinINet settings"); }
                finally { if (!p.HasExited) { p.Kill(); p.WaitForExit(); } }
            }
            for (int i = 0; i < 100 && ProxySnapshot() != before; i++) Thread.Sleep(50);
            Check(ProxySnapshot() == before, "crash watcher restores previous registry values exactly");
            Check(!SystemProxy.IsActive, "restored native proxy is not LinkStack");
            string orphan = Path.Combine(Temp, "orphan");
            using (var p = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--orphan-owner \"" + orphan + "\"") {UseShellExecute = false})) {
                try {
                    for (int i = 0; i < 100 && !File.Exists(orphan); i++) Thread.Sleep(50);
                    Check(File.Exists(orphan) && File.ReadAllText(orphan) == "ready" && SystemProxy.IsActive, "orphan proxy reproduces hidden LAN state without backup");
                } finally { if (!p.HasExited) { p.Kill(); p.WaitForExit(); } }
            }
            for (int i = 0; i < 100 && SystemProxy.IsActive; i++) Thread.Sleep(50);
            Check(!SystemProxy.IsActive, "watcher clears hidden orphan proxy even without snapshot");
            Check(SystemProxy.NativeStateForTests() == nativeBefore, "orphan cleanup leaves original direct LAN proxy configuration intact");
        }
        static async Task Live(bool capture) {
            var links = Link.Scan(); Check(links.Count >= 2, "at least two physical IPv4 uplinks detected");
            var dialer = new NetworkDialer(links, Console.WriteLine);
            foreach (var link in links) using (var timeout = new CancellationTokenSource(25000)) {
                string ip = await Diagnostics.PublicIp(dialer, link, timeout.Token);
                Console.WriteLine(link.Name + " " + link.Address + " → " + ip);
                Check(!string.IsNullOrEmpty(ip), "interface-pinned HTTPS via " + link.Name);
            }
            using (var engine = new ProxyEngine(links.ToArray(), Console.WriteLine)) {
                engine.Start();
                for (int i = 0; i < links.Count * 2; i++) {
                    var req = Downloader.Request(new Uri("https://api.ipify.org/"));
                    using (var response = (HttpWebResponse)await req.GetResponseAsync()) using (var reader = new StreamReader(response.GetResponseStream())) Console.WriteLine("Proxy egress IP: " + await reader.ReadToEndAsync());
                }
                Check(links.All(l => l.Received > 0), "proxy sends real HTTPS payload through every uplink");
                if (capture) {
                    Check(SteamCapture.IsAdmin, "capture test running with administrator rights");
                    string go = Path.Combine(Temp, "go"), result = Path.Combine(Temp, "result");
                    using (var p = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--capture-probe \"" + go + "\" \"" + result + "\"") {UseShellExecute = false})) {
                        try {
                            using (var divert = new SteamCapture(engine, p.Id)) {
                                divert.Start(); File.WriteAllText(go, "go");
                                if (!p.WaitForExit(25000)) { p.Kill(); throw new TimeoutException("Capture probe timeout."); }
                                string output = File.Exists(result) ? File.ReadAllText(result) : "<missing>";
                                Console.WriteLine("Transparent probe: " + output);
                                Check(p.ExitCode == 0, "transparent HTTPS succeeds without proxy settings");
                                Check(divert.Redirected > 0 && divert.Packets > 0, "WinDivert redirects the selected process's actual TCP flow");
                            }
                        } finally { if (!p.HasExited) { p.Kill(); p.WaitForExit(); } }
                    }
                }
            }
        }
        static async Task Run() {
            var a = new Link {Name = "test A", Index = 1, Address = IPAddress.Loopback};
            var b = new Link {Name = "test B", Index = 1, Address = IPAddress.Loopback};
            using (var origin = new Origin()) using (var engine = new ProxyEngine(new[] {a, b}, Console.WriteLine)) {
                engine.Dialer.AllowLoopbackForTests = true; engine.Start();
                string root = "http://127.0.0.1:" + origin.Port;
                var req = Downloader.Request(new Uri(root + "/small"));
                using (var res = (HttpWebResponse)await req.GetResponseAsync()) using (var s = res.GetResponseStream()) {
                    var ms = new MemoryStream(); await s.CopyToAsync(ms); Check(ms.ToArray().SequenceEqual(Payload.Take(1000)), "HTTP proxy forwards bytes");
                }
                await Tunnel(origin.Port, false); await Tunnel(origin.Port, true);
                await SocksNoAuth(); await SocksUnsupported();
                string path = Path.Combine(Temp, "range.bin");
                await new Downloader().Run(root + "/range", path, 4, CancellationToken.None, null);
                Check(File.ReadAllBytes(path).SequenceEqual(Payload), "parallel range file integrity (9 MiB + 17 bytes)");
                Check(a.Connections > 0 && b.Connections > 0 && a.Received > 0 && b.Received > 0, "both scheduler targets carry payload");
                string full = Path.Combine(Temp, "full.bin");
                await new Downloader().Run(root + "/full", full, 4, CancellationToken.None, null);
                Check(File.ReadAllBytes(full).SequenceEqual(Payload), "server without ranges uses single full stream");
                string changed = Path.Combine(Temp, "changed.bin"); bool failed = false;
                try { await new Downloader().Run(root + "/changed", changed, 4, CancellationToken.None, null); } catch { failed = true; }
                Check(failed && !File.Exists(changed) && !Directory.GetFiles(Temp, "*.part").Any(), "changed validator aborts and removes partial file");
                string broken = Path.Combine(Temp, "broken.bin"); failed = false;
                try { await new Downloader().Run(root + "/broken", broken, 4, CancellationToken.None, null); } catch { failed = true; }
                Check(failed && !File.Exists(broken), "incorrect Content-Range is rejected");
                string slow = Path.Combine(Temp, "cancel.bin"); failed = false;
                using (var cancel = new CancellationTokenSource(180)) try { await new Downloader().Run(root + "/slow", slow, 4, cancel.Token, null); } catch { failed = true; }
                Check(failed && !File.Exists(slow) && !Directory.GetFiles(Temp, "*.part").Any(), "cancellation removes partial output");
                using (var c = new TcpClient()) {
                    await c.ConnectAsync(IPAddress.Loopback, ProxyEngine.HttpPort); var s = c.GetStream();
                    byte[] h = Encoding.ASCII.GetBytes("GET nonsense HTTP/1.1\r\nHost: nowhere\r\n\r\n"); await s.WriteAsync(h, 0, h.Length); byte[] response = new byte[300]; int n = await s.ReadAsync(response, 0, response.Length);
                    Check(Encoding.ASCII.GetString(response, 0, n).Contains("400"), "malformed proxy request rejected");
                }
                var normal = new NetworkDialer(new[] {a}, null); failed = false;
                try { await normal.Connect("127.0.0.1", ProxyEngine.HttpPort, CancellationToken.None, null); } catch (InvalidOperationException) { failed = true; }
                Check(failed, "production proxy rejects recursive localhost requests");
                engine.Dispose();
                using (var c = new TcpClient()) { failed = false; try { await c.ConnectAsync(IPAddress.Loopback, ProxyEngine.HttpPort); } catch (SocketException) { failed = true; } Check(failed, "stop closes listening port"); }
            }
        }
        static async Task ReadExact(Stream s, byte[] b) { int at = 0; while (at < b.Length) { int n = await s.ReadAsync(b, at, b.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; } }
        static async Task Tunnel(int port, bool socks) {
            using (var c = new TcpClient()) {
                await c.ConnectAsync(IPAddress.Loopback, socks ? ProxyEngine.SocksPort : ProxyEngine.HttpPort); var s = c.GetStream();
                if (socks) {
                    byte[] greet = {5, 1, 0}; foreach (byte v in greet) { await s.WriteAsync(new[] {v}, 0, 1); await Task.Delay(2); }
                    byte[] r = new byte[2]; await ReadExact(s, r); Check(r.SequenceEqual(new byte[] {5, 0}), "fragmented SOCKS5 greeting accepted");
                    byte[] connect = {5, 1, 0, 1, 127, 0, 0, 1, (byte)(port >> 8), (byte)port}; await s.WriteAsync(connect, 0, connect.Length);
                    byte[] reply = new byte[10]; await ReadExact(s, reply); Check(reply[1] == 0, "SOCKS5 CONNECT succeeds");
                } else {
                    byte[] h = Encoding.ASCII.GetBytes("CONNECT 127.0.0.1:" + port + " HTTP/1.1\r\nHost: test\r\n\r\n"); await s.WriteAsync(h, 0, h.Length);
                    var header = new MemoryStream(); int tail = 0;
                    while (tail != 0x0d0a0d0a) { byte[] v = new byte[1]; await ReadExact(s, v); header.WriteByte(v[0]); tail = (tail << 8) | v[0]; }
                    Check(Encoding.ASCII.GetString(header.ToArray()).Contains("200 Connection Established"), "HTTP CONNECT succeeds");
                }
                byte[] get = Encoding.ASCII.GetBytes("GET /small HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"); await s.WriteAsync(get, 0, get.Length);
                c.Client.Shutdown(SocketShutdown.Send);
                var data = new MemoryStream(); await s.CopyToAsync(data); byte[] bytes = data.ToArray();
                Check(bytes.Skip(bytes.Length - 1000).SequenceEqual(Payload.Take(1000)), (socks ? "SOCKS5" : "HTTP CONNECT") + " preserves payload after client half-close");
            }
        }
        static async Task SocksNoAuth() {
            using (var c = new TcpClient()) { await c.ConnectAsync(IPAddress.Loopback, ProxyEngine.SocksPort); byte[] b = {5,1,2}; await c.GetStream().WriteAsync(b, 0, b.Length); byte[] reply = new byte[2]; await ReadExact(c.GetStream(), reply); Check(reply[1] == 255, "unsupported SOCKS auth rejected"); }
        }
        static async Task SocksUnsupported() {
            using (var c = new TcpClient()) { await c.ConnectAsync(IPAddress.Loopback, ProxyEngine.SocksPort); byte[] b = {5,1,0,5,3,0,1}; await c.GetStream().WriteAsync(b,0,b.Length); byte[] reply = new byte[12]; await ReadExact(c.GetStream(), reply); Check(reply[3] == 7, "SOCKS UDP ASSOCIATE explicitly rejected"); }
        }
        sealed class Origin : IDisposable {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly CancellationTokenSource cancel = new CancellationTokenSource();
            public int Port;
            public Origin() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; Accept(); }
            async void Accept() { while (!cancel.IsCancellationRequested) { try { var c = await listener.AcceptTcpClientAsync(); ServeAsync(c); } catch { break; } } }
            async void ServeAsync(TcpClient c) { await Task.Run(() => Serve(c)); }
            void Serve(TcpClient c) {
                using (c) try {
                    c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
                    var s = c.GetStream(); var header = new MemoryStream(); int tail = 0;
                    while (tail != 0x0d0a0d0a && header.Length < 32768) { int v = s.ReadByte(); if (v < 0) return; header.WriteByte((byte)v); tail = (tail << 8) | v; }
                    string text = Encoding.ASCII.GetString(header.ToArray()); string path = text.Split(' ')[1];
                    int count = path == "/small" ? 1000 : Payload.Length; long from = 0, to = count - 1;
                    var range = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^Range: bytes=(\d+)-(\d+)\r?$");
                    bool partial = range.Success && path != "/full";
                    if (partial) { from = long.Parse(range.Groups[1].Value); to = Math.Min(long.Parse(range.Groups[2].Value), count - 1); }
                    string etag = path == "/changed" && to > 0 ? "\"changed\"" : "\"test\"";
                    string output = "HTTP/1.1 " + (partial ? "206 Partial Content" : "200 OK") + "\r\nContent-Length: " + (to - from + 1) + "\r\nETag: " + etag + "\r\nConnection: close\r\n";
                    if (partial) output += "Content-Range: bytes " + (path == "/broken" && to > 0 ? from + 1 : from) + "-" + to + "/" + count + "\r\n";
                    byte[] h = Encoding.ASCII.GetBytes(output + "\r\n"); s.Write(h,0,h.Length);
                    for (long i = from; i <= to; i += 65536) { int n = (int)Math.Min(65536, to - i + 1); s.Write(Payload, (int)i, n); if (path == "/slow") Thread.Sleep(30); }
                } catch {} }
            public void Dispose() { cancel.Cancel(); listener.Stop(); }
        }
    }
}
