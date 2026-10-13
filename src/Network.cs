using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LinkStack {
    public sealed class Link {
        public string Name, Description;
        public IPAddress Address;
        public int Index, Weight = 1;
        public long Received, Sent, Connections, Failures;
        public int Active;
        public double Rate;
        public override string ToString() { return Name + " · " + Address; }
        public static List<Link> Scan() {
            var result = new List<Link>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)) continue;
                string desc = nic.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("npcap") || desc.Contains("vpn") || desc.Contains("wintun")) continue;
                var p = nic.GetIPProperties();
                if (!p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                var ip = p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."));
                if (ip == null) continue;
                result.Add(new Link { Name = nic.Name, Description = nic.Description, Address = ip.Address, Index = p.GetIPv4Properties().Index });
            }
            return result.OrderBy(x => x.Name.StartsWith("Ethernet") ? 0 : 1).ToList();
        }
        public static bool HasVpn() {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up &&
                (n.NetworkInterfaceType == NetworkInterfaceType.Ppp || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel || n.Description.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }

    public sealed class Dialed : IDisposable {
        public Socket Socket;
        public Link Link;
        public void Dispose() { if (Socket != null) { try { Socket.Dispose(); } catch {} Socket = null; } }
    }

    public sealed class NetworkDialer {
        public readonly Link[] Links;
        public Action<string> Log;
        internal bool AllowLoopbackForTests = false;
        int cursor;
        public NetworkDialer(IEnumerable<Link> links, Action<string> log) { Links = links.ToArray(); Log = log; if (Links.Length == 0) throw new ArgumentException("Wybierz co najmniej jedno łącze."); }
        public async Task<Dialed> Connect(string host, int port, CancellationToken token, Link only) {
            if (port < 1 || port > 65535) throw new ArgumentException("Niepoprawny port.");
            IPAddress literal;
            var ips = IPAddress.TryParse(host, out literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(host);
            ips = ips.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (ips.Length == 0) throw new SocketException((int)SocketError.AddressFamilyNotSupported);
            if (ips.Any(ip => ip.Equals(IPAddress.Any) || (!AllowLoopbackForTests && IPAddress.IsLoopback(ip)))) throw new InvalidOperationException("Proxy nie przekazuje ruchu z powrotem na localhost.");
            int total = Links.Sum(l => Math.Max(1, l.Weight));
            int ticket = (Interlocked.Increment(ref cursor) & int.MaxValue) % total;
            int first = 0;
            for (int i = 0; i < Links.Length; i++) { ticket -= Math.Max(1, Links[i].Weight); if (ticket < 0) { first = i; break; } }
            Exception last = null;
            for (int attempt = 0; attempt < (only == null ? Links.Length : 1); attempt++) {
                Link link = only ?? Links[(first + attempt) % Links.Length];
                foreach (var ip in ips.Take(3)) {
                    token.ThrowIfCancellationRequested();
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try {
                        socket.NoDelay = true;
                        // Winsock requires the IPv4 interface index in network byte order.
                        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(link.Index));
                        socket.Bind(new IPEndPoint(link.Address, 0));
                        using (token.Register(() => { try { socket.Dispose(); } catch {} })) {
                            Task connect = Task.Factory.FromAsync(socket.BeginConnect, socket.EndConnect, new IPEndPoint(ip, port), null);
                            if (await Task.WhenAny(connect, Task.Delay(5000, token)) != connect) {
                                socket.Dispose();
                                try { await connect; } catch {}
                                token.ThrowIfCancellationRequested();
                                throw new TimeoutException("Brak odpowiedzi przez " + link.Name);
                            }
                            await connect;
                        }
                        Interlocked.Increment(ref link.Connections);
                        return new Dialed { Socket = socket, Link = link };
                    } catch (Exception e) {
                        socket.Dispose(); token.ThrowIfCancellationRequested(); last = e;
                    }
                }
                Interlocked.Increment(ref link.Failures);
                if (Log != null) Log("Nie udało się połączyć przez " + link.Name + ". " + (only == null ? "Próbuję kolejne łącze." : ""));
            }
            throw last ?? new InvalidOperationException("Brak dostępnej trasy.");
        }
    }
}
