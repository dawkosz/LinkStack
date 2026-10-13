using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinkStack {
    public sealed class DownloadProgress {
        public long Done, Total;
        public string Status;
    }
    public sealed class Downloader {
        public static HttpWebRequest Request(Uri uri) {
            if (uri.Scheme != "http" && uri.Scheme != "https") throw new ArgumentException("Podaj adres HTTP lub HTTPS.");
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Proxy = new WebProxy("http://127.0.0.1:" + ProxyEngine.HttpPort, false);
            req.Timeout = 20000; req.ReadWriteTimeout = 30000;
            req.KeepAlive = false; req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.None;
            req.Headers["Accept-Encoding"] = "identity";
            req.UserAgent = "LinkStack/1.0";
            req.ServicePoint.ConnectionLimit = 64;
            return req;
        }
        static bool ParseRange(string header, out long from, out long to, out long total) {
            from = to = total = 0;
            var match = Regex.Match(header ?? "", @"^bytes (\d+)-(\d+)/(\d+)$");
            return match.Success && long.TryParse(match.Groups[1].Value, out from) && long.TryParse(match.Groups[2].Value, out to) && long.TryParse(match.Groups[3].Value, out total) && total > to && to >= from;
        }
        public async Task Run(string url, string destination, int workers, CancellationToken token, Action<DownloadProgress> progress) {
            Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) throw new ArgumentException("Niepoprawny adres URL.");
            if (File.Exists(destination)) throw new IOException("Plik już istnieje. Wybierz inną nazwę.");
            string partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            long size = 0, done = 0; bool ranges = false; string etag = null, modified = null;
            var probe = Request(uri); probe.AddRange(0L, 0L);
            try {
                using (token.Register(probe.Abort)) using (var response = (HttpWebResponse)await probe.GetResponseAsync()) {
                    long from, to, total;
                    uri = response.ResponseUri;
                    ranges = response.StatusCode == HttpStatusCode.PartialContent && ParseRange(response.Headers["Content-Range"], out from, out to, out total) && from == 0 && to == 0;
                    if (ranges) { ParseRange(response.Headers["Content-Range"], out from, out to, out size); }
                    else size = response.ContentLength;
                    etag = response.Headers["ETag"]; modified = response.Headers["Last-Modified"];
                    if (string.IsNullOrEmpty(etag) || etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) etag = null;
                    // Without a validator, different requests could represent different versions.
                    ranges = ranges && (!string.IsNullOrEmpty(etag) || !string.IsNullOrEmpty(modified));
                }
            } catch (WebException e) {
                var r = e.Response as HttpWebResponse;
                if (r == null || r.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable) { if (r != null) r.Close(); throw; }
                r.Close(); ranges = false;
            }
            token.ThrowIfCancellationRequested();
            string mode = ranges ? "Pobieranie w częściach przez wybrane łącza" : "Serwer bez bezpiecznych zakresów — jedno połączenie";
            if (progress != null) progress(new DownloadProgress {Total = size, Status = mode});
            try {
                if (!ranges) {
                    var req = Request(uri);
                    using (token.Register(req.Abort)) using (var res = (HttpWebResponse)await req.GetResponseAsync()) {
                        if (res.StatusCode != HttpStatusCode.OK) throw new IOException("Serwer nie zwrócił pełnego pliku.");
                        size = res.ContentLength;
                        using (var input = res.GetResponseStream()) using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                            var b = new byte[65536]; int n;
                            while ((n = await input.ReadAsync(b, 0, b.Length, token)) != 0) { await file.WriteAsync(b, 0, n, token); done += n; if (progress != null) progress(new DownloadProgress {Done = done, Total = size, Status = mode}); }
                            if (size >= 0 && done != size) throw new IOException("Niekompletny plik.");
                            file.Flush(true);
                        }
                    }
                } else {
                    const long chunkSize = 4L * 1024 * 1024;
                    long nextChunk = -1, chunks = (size + chunkSize - 1) / chunkSize;
                    var writeLock = new SemaphoreSlim(1);
                    using (var failure = CancellationTokenSource.CreateLinkedTokenSource(token))
                    using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                        file.SetLength(size);
                        var tasks = Enumerable.Range(0, Math.Min(Math.Max(workers, 2), 16)).Select(async worker => {
                            try {
                                while (true) {
                                    long chunk = Interlocked.Increment(ref nextChunk); if (chunk >= chunks) return;
                                    long start = chunk * chunkSize, end = Math.Min(size - 1, start + chunkSize - 1);
                                    byte[] data = null;
                                    for (int attempt = 0; attempt < 3; attempt++) {
                                        failure.Token.ThrowIfCancellationRequested();
                                        var req = Request(uri); req.AddRange(start, end); req.Headers["If-Range"] = etag ?? modified;
                                        try {
                                            using (failure.Token.Register(req.Abort)) using (var res = (HttpWebResponse)await req.GetResponseAsync()) {
                                                long a, b, length;
                                                if (res.StatusCode != HttpStatusCode.PartialContent || !ParseRange(res.Headers["Content-Range"], out a, out b, out length) || a != start || b != end || length != size)
                                                    throw new InvalidDataException("Serwer zmienił plik lub zignorował zakres. Pobieranie zatrzymane.");
                                                if (etag != null && res.Headers["ETag"] != etag) throw new InvalidDataException("Plik zmienił się w trakcie pobierania.");
                                                if (etag == null && res.Headers["Last-Modified"] != modified) throw new InvalidDataException("Data pliku zmieniła się w trakcie pobierania.");
                                                string coding = res.Headers["Content-Encoding"];
                                                if (!string.IsNullOrEmpty(coding) && coding != "identity") throw new InvalidDataException("Serwer kompresuje zakresy.");
                                                data = new byte[(int)(end - start + 1)]; int at = 0;
                                                using (var input = res.GetResponseStream()) {
                                                    while (at < data.Length) { int n = await input.ReadAsync(data, at, data.Length - at, failure.Token); if (n == 0) throw new EndOfStreamException("Przerwany zakres."); at += n; }
                                                    byte[] extra = new byte[1]; if (await input.ReadAsync(extra, 0, 1, failure.Token) != 0) throw new InvalidDataException("Zakres za długi.");
                                                }
                                            }
                                            break;
                                        } catch (InvalidDataException) { throw; }
                                        catch { failure.Token.ThrowIfCancellationRequested(); if (attempt == 2) throw; }
                                        await Task.Delay(500, failure.Token);
                                    }
                                    await writeLock.WaitAsync(failure.Token);
                                    try { file.Position = start; await file.WriteAsync(data, 0, data.Length, failure.Token); } finally { writeLock.Release(); }
                                    long completed = Interlocked.Add(ref done, data.Length);
                                    if (progress != null) progress(new DownloadProgress {Done = completed, Total = size, Status = mode});
                                }
                            } catch { failure.Cancel(); throw; }
                        }).ToArray();
                        await Task.WhenAll(tasks); file.Flush(true);
                    }
                    if (done != size) throw new IOException("Niekompletny plik.");
                }
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination);
                if (progress != null) progress(new DownloadProgress {Done = done, Total = done, Status = "Gotowe · " + Path.GetFileName(destination)});
            } catch { if (File.Exists(partial)) File.Delete(partial); throw; }
        }
    }
}
