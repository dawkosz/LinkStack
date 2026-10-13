using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinkStack {
    public static class Diagnostics {
        public static async Task<string> PublicIp(NetworkDialer dialer, Link link, CancellationToken token) {
            using (var d = await dialer.Connect("api.ipify.org", 443, token, link))
            using (var ns = new System.Net.Sockets.NetworkStream(d.Socket, false))
            using (var tls = new SslStream(ns, false))
            using (token.Register(() => { try { d.Dispose(); } catch {} })) {
                await tls.AuthenticateAsClientAsync("api.ipify.org", null, SslProtocols.Tls12, true);
                byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: api.ipify.org\r\nUser-Agent: LinkStack/1.0\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(request, 0, request.Length, token);
                var buffer = new byte[4096]; var output = new MemoryStream(); int n;
                while ((n = await tls.ReadAsync(buffer, 0, buffer.Length, token)) > 0) { output.Write(buffer, 0, n); if (output.Length > 16384) throw new IOException("Zbyt długa odpowiedź diagnostyczna."); }
                string raw = Encoding.ASCII.GetString(output.ToArray());
                if (!raw.StartsWith("HTTP/1.1 200") && !raw.StartsWith("HTTP/1.0 200")) throw new IOException("Serwis diagnostyczny nie odpowiedział poprawnie.");
                int split = raw.IndexOf("\r\n\r\n"); if (split < 0) throw new IOException("Niepoprawna odpowiedź.");
                string body = raw.Substring(split + 4).Trim();
                if (raw.Substring(0, split).IndexOf("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase) >= 0) {
                    int line = body.IndexOf("\r\n"); if (line < 0) throw new IOException("Niepoprawny chunk.");
                    int size = Convert.ToInt32(body.Substring(0, line), 16); body = body.Substring(line + 2, size);
                }
                System.Net.IPAddress ip;
                if (!System.Net.IPAddress.TryParse(body, out ip)) throw new IOException("Brak adresu IP w odpowiedzi.");
                return ip.ToString();
            }
        }
    }
}
