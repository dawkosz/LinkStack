using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkStack {
    public sealed class MainForm : Form {
        static readonly Color Bg = Color.FromArgb(16, 21, 30), PanelBg = Color.FromArgb(25, 33, 46), Muted = Color.FromArgb(155, 172, 195), Accent = Color.FromArgb(72, 222, 173);
        readonly DataGridView grid = new DataGridView();
        readonly Label speed = new Label(), status = new Label(), count = new Label(), downloadStatus = new Label();
        readonly Button start = new Button(), refresh = new Button(), test = new Button(), download = new Button(), cancel = new Button();
        readonly CheckBox steam = new CheckBox(), systemProxy = new CheckBox();
        readonly RichTextBox log = new RichTextBox();
        readonly TextBox url = new TextBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Sparkline graph = new Sparkline();
        List<Link> links = new List<Link>(); ProxyEngine engine; SteamCapture capture;
        CancellationTokenSource downloadStop;
        bool ownProxy, busy, closing;
        readonly Dictionary<int, long> previous = new Dictionary<int, long>();
        DateTime lastTick = DateTime.UtcNow;
        readonly object logLock = new object();
        readonly Queue<string> pending = new Queue<string>();
        readonly string diagnosticDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkStack");
        DateTime lastExport = DateTime.MinValue;
        public MainForm(bool steamMode) : this(steamMode, false, null, false) {}
        public MainForm(bool steamMode, bool autoStart, string selectedConfiguration, bool restoreSystemProxy) {
            Icon = Brand.CreateIcon();
            Text = "LinkStack · Twoje łącza, jedna aplikacja"; BackColor = Bg; ForeColor = Color.White;
            Font = new Font("Segoe UI", 10); Size = new Size(1080, 860); MinimumSize = new Size(920, 760); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel {Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);
            var header = new Panel {Dock = DockStyle.Fill};
            header.Controls.Add(Label("LinkStack", 25, Color.White, new Rectangle(0, 0, 270, 39)));
            header.Controls.Add(Label("Kabel + Wi-Fi · lokalnie, bez abonamentu", 10, Muted, new Rectangle(2, 41, 500, 23)));
            status.Width = 300; status.Dock = DockStyle.Right; status.TextAlign = ContentAlignment.MiddleRight; status.ForeColor = Muted; status.Text = "GOTOWY DO STARTU"; header.Controls.Add(status); root.Controls.Add(header, 0, 0);
            var summary = new Panel {Dock = DockStyle.Fill, BackColor = PanelBg, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 12)};
            summary.Controls.Add(Label("ŁĄCZNE POBIERANIE W LINKSTACK", 9, Muted, new Rectangle(20, 13, 400, 20)));
            speed.Bounds = new Rectangle(17, 34, 350, 55); speed.Font = new Font("Segoe UI", 30, FontStyle.Bold); speed.ForeColor = Accent; speed.Text = "0,00 MB/s"; summary.Controls.Add(speed);
            count.Bounds = new Rectangle(22, 92, 500, 22); count.ForeColor = Muted; count.Text = "Brak aktywnych połączeń"; summary.Controls.Add(count);
            graph.Width = 390; graph.Dock = DockStyle.Right; summary.Controls.Add(graph); root.Controls.Add(summary, 0, 1);
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = PanelBg; grid.BorderStyle = BorderStyle.None; grid.GridColor = Bg; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = PanelBg; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold); grid.ColumnHeadersHeight = 34;
            grid.DefaultCellStyle.BackColor = PanelBg; grid.DefaultCellStyle.ForeColor = Color.White; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 55, 73); grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.RowTemplate.Height = 38; grid.RowHeadersVisible = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name = "use", HeaderText = "Używaj", FillWeight = 55});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "name", HeaderText = "Łącze", ReadOnly = true, FillWeight = 100});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "ip", HeaderText = "Adres IPv4", ReadOnly = true, FillWeight = 125});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "weight", HeaderText = "Waga 1–10", FillWeight = 65});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "rate", HeaderText = "Pobieranie", ReadOnly = true, FillWeight = 110});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "bytes", HeaderText = "Pobrano", ReadOnly = true, FillWeight = 90});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "active", HeaderText = "Połączenia", ReadOnly = true, FillWeight = 80});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name = "public", HeaderText = "Publiczne IP / test", ReadOnly = true, FillWeight = 170});
            root.Controls.Add(grid, 0, 2);
            var actions = new FlowLayoutPanel {Dock = DockStyle.Fill, Padding = new Padding(0, 9, 0, 0), WrapContents = true};
            Button(start, "Włącz łączenie", 165, Accent); Button(refresh, "Odśwież karty", 145, PanelBg); Button(test, "Sprawdź oba łącza", 185, PanelBg);
            start.Click += StartStop; refresh.Click += (s, e) => Scan(); test.Click += TestLinks;
            actions.Controls.Add(start); actions.Controls.Add(refresh); actions.Controls.Add(test);
            steam.Text = "Tryb Steam (administrator)"; steam.Checked = steamMode; steam.AutoSize = true; steam.Margin = new Padding(10, 11, 5, 0); actions.Controls.Add(steam);
            systemProxy.Text = "Proxy Windows dla przeglądarek"; systemProxy.AutoSize = true; systemProxy.Margin = new Padding(0, 4, 10, 0); actions.Controls.Add(systemProxy);
            systemProxy.Checked = restoreSystemProxy;
            root.Controls.Add(actions, 0, 3);
            var down = new TableLayoutPanel {Dock = DockStyle.Fill, BackColor = PanelBg, Padding = new Padding(12), RowCount = 3, ColumnCount = 3};
            down.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); down.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); down.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            down.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); down.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); down.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            url.Dock = DockStyle.Fill; url.BackColor = Bg; url.ForeColor = Color.White; url.BorderStyle = BorderStyle.FixedSingle; url.Text = "https://"; url.Margin = new Padding(0, 4, 10, 3);
            Button(download, "Pobierz plik", 130, Accent); download.Dock = DockStyle.Fill; download.Click += Download; Button(cancel, "Anuluj", 90, PanelBg); cancel.Dock = DockStyle.Fill; cancel.Enabled = false; cancel.Click += (s,e) => { if (downloadStop != null) downloadStop.Cancel(); };
            down.Controls.Add(url, 0, 0); down.Controls.Add(download, 1, 0); down.Controls.Add(cancel, 2, 0);
            progress.Dock = DockStyle.Fill; down.Controls.Add(progress, 0, 1); down.SetColumnSpan(progress, 3);
            downloadStatus.Dock = DockStyle.Fill; downloadStatus.ForeColor = Muted; downloadStatus.Text = "Wklej bezpośredni URL pliku. Pobieranie w częściach, gdy serwer obsługuje zakresy."; down.Controls.Add(downloadStatus, 0, 2); down.SetColumnSpan(downloadStatus, 3); root.Controls.Add(down, 0, 4);
            log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BorderStyle = BorderStyle.None; log.BackColor = Bg; log.ForeColor = Muted; log.Font = new Font("Consolas", 9); log.Margin = new Padding(0, 10, 0, 0); root.Controls.Add(log, 0, 5);
            var footer = Label("HTTP 127.0.0.1:17881   ·   SOCKS5 127.0.0.1:17880   ·   Kilka połączeń = szansa na sumę prędkości", 9, Muted, Rectangle.Empty); footer.Dock = DockStyle.Fill; root.Controls.Add(footer, 0, 6);
            timer.Interval = 750; timer.Tick += Tick; timer.Start();
            FormClosing += Close; Shown += (s,e) => {
                Scan();
                if (selectedConfiguration != null && selectedConfiguration != "all") {
                    var selection = new Dictionary<int, int>();
                    foreach (string item in selectedConfiguration.Split(',')) {
                        string[] values = item.Split(':'); int index, weight;
                        if (values.Length == 2 && int.TryParse(values[0], out index) && int.TryParse(values[1], out weight) && weight >= 1 && weight <= 10) selection[index] = weight;
                    }
                    for (int i = 0; i < links.Count; i++) { grid.Rows[i].Cells[0].Value = selection.ContainsKey(links[i].Index); if (selection.ContainsKey(links[i].Index)) grid.Rows[i].Cells[3].Value = selection[links[i].Index]; }
                }
                if (autoStart) BeginInvoke(new Action(() => StartStop(this, EventArgs.Empty)));
            };
            AddLog("LinkStack 1.0.2 · własny silnik C#, bez konta i limitu transferu.");
        }
        static Label Label(string text, float size, Color color, Rectangle bounds) { return new Label {Text = text, Font = new Font("Segoe UI", size), ForeColor = color, Bounds = bounds}; }
        static void Button(Button b, string text, int width, Color color) { b.Text = text; b.Width = width; b.Height = 36; b.BackColor = color; b.ForeColor = color == Accent ? Bg : Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Margin = new Padding(0, 0, 8, 5); b.Cursor = Cursors.Hand; }
        void AddLog(string text) {
            lock (logLock) {
                string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
                if (pending.Count < 200) pending.Enqueue(line);
                try {
                    Directory.CreateDirectory(diagnosticDir);
                    string path = Path.Combine(diagnosticDir, "events.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1048576) File.WriteAllText(path, "");
                    File.AppendAllText(path, line + Environment.NewLine);
                } catch {}
            }
        }
        void Scan() {
            if (engine != null) return;
            try {
                links = Link.Scan(); previous.Clear(); grid.Rows.Clear();
                foreach (var l in links) grid.Rows.Add(true, l.Name, l.Address, l.Weight, "0,00 MB/s", "0 MB", "0", "Jeszcze nie sprawdzono");
                AddLog("Wykryto " + links.Count + " aktywne fizyczne łącza IPv4.");
                if (Link.HasVpn()) AddLog("VPN jest aktywny. Rozłącz go przed testem sumowania łączy.");
            } catch (Exception e) { AddLog(e.Message); }
        }
        Link[] Selected() {
            grid.EndEdit(); var selected = new List<Link>();
            for (int i = 0; i < links.Count; i++) if (Convert.ToBoolean(grid.Rows[i].Cells[0].Value)) {
                int weight; if (!int.TryParse(Convert.ToString(grid.Rows[i].Cells[3].Value), out weight) || weight < 1 || weight > 10) throw new ArgumentException("Waga łącza musi być liczbą od 1 do 10.");
                links[i].Weight = weight; selected.Add(links[i]);
            }
            if (selected.Count == 0) throw new ArgumentException("Wybierz co najmniej jedno łącze."); return selected.ToArray();
        }
        void StartStop(object sender, EventArgs args) {
            if (engine != null) { Stop(); return; }
            try {
                var chosen = Selected();
                if (Link.HasVpn()) {
                    MessageBox.Show(this, "Rozłącz VPN przed łączeniem kabla i Wi-Fi. LinkStack kieruje ruch przez wybrane fizyczne karty.", "VPN jest aktywny", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
                }
                if (steam.Checked && !SteamCapture.IsAdmin) {
                    string configuration = string.Join(",", chosen.Select(l => l.Index + ":" + l.Weight));
                    var pi = new ProcessStartInfo(Application.ExecutablePath, "--steam-start " + configuration + (systemProxy.Checked ? " --with-system-proxy" : "")) {UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory};
                    Process.Start(pi); Close(); return;
                }
                engine = new ProxyEngine(chosen, AddLog); engine.Start();
                if (steam.Checked) { capture = new SteamCapture(engine); capture.Start(); AddLog("Tryb Steam: nowe połączenia IPv4 TCP 80/443. Wznów pobieranie, żeby Steam zestawił nowe połączenia."); }
                if (systemProxy.Checked) { SystemProxy.Enable(); ownProxy = true; AddLog("Włączono proxy Windows. Poprzednie ustawienia wrócą po zatrzymaniu."); }
                grid.Enabled = refresh.Enabled = steam.Enabled = systemProxy.Enabled = false;
                start.Text = "Zatrzymaj"; start.BackColor = Color.FromArgb(255, 149, 133); status.Text = "ŁĄCZENIE AKTYWNE"; status.ForeColor = Accent;
                AddLog("Aktywne: " + string.Join(" + ", chosen.Select(l => l.Name)) + ". Proxy HTTP i SOCKS5 gotowe.");
                if (chosen.Length == 1) AddLog("Wybrane jedno łącze — nie będzie sumowania prędkości.");
            } catch (Exception e) { Stop(); MessageBox.Show(this, e.Message, "Nie udało się uruchomić", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        void Stop() {
            if (downloadStop != null) downloadStop.Cancel();
            if (capture != null) { capture.Dispose(); capture = null; }
            if (ownProxy) { try { SystemProxy.Recover(true); ownProxy = false; } catch (Exception e) { AddLog("Przywracanie proxy: " + e.Message); } }
            if (engine != null) {
                engine.Dispose(); engine = null;
                try { SystemProxy.Recover(false); } catch (Exception e) { AddLog("Sprzątanie proxy: " + e.Message); }
                AddLog("Zatrzymano. Kabel i Wi-Fi wracają do zwykłego routingu Windows.");
            }
            grid.Enabled = refresh.Enabled = steam.Enabled = systemProxy.Enabled = true;
            start.Text = "Włącz łączenie"; start.BackColor = Accent; status.Text = "ZATRZYMANY"; status.ForeColor = Muted;
        }
        async void TestLinks(object sender, EventArgs e) {
            if (busy) return;
            try {
                var chosen = Selected(); busy = true; test.Enabled = false;
                var dialer = new NetworkDialer(chosen, AddLog);
                using (var timeout = new CancellationTokenSource(20000)) {
                    await Task.WhenAll(chosen.Select(async l => {
                        string text;
                        try { text = await Diagnostics.PublicIp(dialer, l, timeout.Token); AddLog(l.Name + " → publiczne IP " + text); }
                        catch (Exception ex) { text = "Błąd połączenia"; AddLog(l.Name + ": " + ex.Message); }
                        if (!closing) for (int i = 0; i < links.Count; i++) if (links[i] == l) grid.Rows[i].Cells[7].Value = text;
                    }));
                }
            } catch (Exception ex) { AddLog(ex.Message); }
            finally { busy = false; if (!closing) test.Enabled = true; }
        }
        async void Download(object sender, EventArgs e) {
            if (downloadStop != null) return;
            if (engine == null) { MessageBox.Show(this, "Najpierw kliknij Włącz łączenie."); return; }
            Uri uri;
            if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { MessageBox.Show(this, "Podaj bezpośredni adres pliku HTTP lub HTTPS."); return; }
            using (var save = new SaveFileDialog {FileName = Path.GetFileName(uri.LocalPath), InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), OverwritePrompt = true}) {
                if (string.IsNullOrEmpty(save.FileName)) save.FileName = "pobrany-plik.bin";
                if (save.ShowDialog(this) != DialogResult.OK) return;
                if (File.Exists(save.FileName)) { MessageBox.Show(this, "Wybierz nową nazwę pliku. LinkStack nie nadpisuje istniejących plików."); return; }
                downloadStop = new CancellationTokenSource(); download.Enabled = false; cancel.Enabled = true; url.Enabled = false; progress.Value = 0;
                var reporter = new Progress<DownloadProgress>(p => {
                    if (closing) return;
                    progress.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100.0 / p.Total) : 0;
                    downloadStatus.Text = p.Status + " · " + Bytes(p.Done) + (p.Total > 0 ? " / " + Bytes(p.Total) : "");
                });
                try { await new Downloader().Run(uri.ToString(), save.FileName, 8, downloadStop.Token, p => ((IProgress<DownloadProgress>)reporter).Report(p)); AddLog("Zapisano " + save.FileName); }
                catch (Exception ex) { downloadStatus.Text = downloadStop.IsCancellationRequested ? "Anulowano pobieranie" : "Błąd: " + ex.Message; AddLog(downloadStatus.Text); }
                finally { downloadStop.Dispose(); downloadStop = null; if (!closing) { download.Enabled = url.Enabled = true; cancel.Enabled = false; } }
            }
        }
        static string Bytes(long n) { return n >= 1073741824 ? (n / 1073741824.0).ToString("F2") + " GB" : (n / 1048576.0).ToString("F1") + " MB"; }
        void Tick(object sender, EventArgs e) {
            double elapsed = Math.Max(.1, (DateTime.UtcNow - lastTick).TotalSeconds); lastTick = DateTime.UtcNow; double totalRate = 0; long total = 0; int active = 0;
            for (int i = 0; i < links.Count; i++) {
                var l = links[i]; long bytes = Interlocked.Read(ref l.Received), prior;
                previous.TryGetValue(l.Index, out prior); l.Rate = Math.Max(0, (bytes - prior) / elapsed / 1048576.0); previous[l.Index] = bytes;
                totalRate += l.Rate; total += bytes; active += l.Active;
                grid.Rows[i].Cells[4].Value = l.Rate.ToString("F2") + " MB/s"; grid.Rows[i].Cells[5].Value = Bytes(bytes); grid.Rows[i].Cells[6].Value = l.Active + " / " + l.Connections;
            }
            speed.Text = totalRate.ToString("F2") + " MB/s";
            count.Text = Bytes(total) + "  ·  " + active + " aktywnych" + (capture != null ? "  ·  Steam: " + capture.Redirected + " przechwyconych" : ""); graph.Push((float)totalRate);
            lock (logLock) while (pending.Count > 0) log.AppendText(pending.Dequeue() + Environment.NewLine);
            if (log.TextLength > 25000) log.Text = log.Text.Substring(log.TextLength - 18000);
            log.SelectionStart = log.TextLength; log.ScrollToCaret();
            if (capture != null && !capture.Running) { AddLog("Tryb Steam przerwał pracę. Zatrzymuję silnik."); Stop(); }
            if ((DateTime.UtcNow - lastExport).TotalSeconds >= 2) {
                lastExport = DateTime.UtcNow;
                try {
                    Directory.CreateDirectory(diagnosticDir);
                    var lines = new List<string> {"version=1.0.2", "updated=" + DateTime.UtcNow.ToString("o"), "admin=" + SteamCapture.IsAdmin,
                        "proxy_running=" + (engine != null && engine.Running), "steam_running=" + (capture != null && capture.Running),
                        "steam_connections=" + (capture == null ? 0 : capture.Redirected), "steam_packets=" + (capture == null ? 0 : capture.Packets)};
                    foreach (var link in links) lines.Add("link=" + link.Name + ";ip=" + link.Address + ";received=" + Interlocked.Read(ref link.Received) + ";MBps=" + link.Rate.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ";active=" + link.Active + ";connections=" + link.Connections);
                    File.WriteAllLines(Path.Combine(diagnosticDir, "status.txt"), lines);
                } catch {}
            }
        }
        void Close(object sender, FormClosingEventArgs e) { closing = true; timer.Stop(); Stop(); }
    }
    public sealed class Sparkline : Control {
        readonly Queue<float> data = new Queue<float>();
        public Sparkline() { DoubleBuffered = true; }
        public void Push(float value) { data.Enqueue(value); while (data.Count > 60) data.Dequeue(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(44, 57, 75))) for (int i = 1; i <= 3; i++) e.Graphics.DrawLine(p, 0, Height * i / 4, Width, Height * i / 4);
            if (data.Count < 2) return; var values = data.ToArray(); float max = Math.Max(1, values.Max() * 1.15f);
            var points = values.Select((v,i) => new PointF(Width * i / 59f, Height - 4 - v / max * (Height - 8))).ToArray();
            using (var p = new Pen(Color.FromArgb(72, 222, 173), 2)) e.Graphics.DrawLines(p, points);
        }
    }
}
