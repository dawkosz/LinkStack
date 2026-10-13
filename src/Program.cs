using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("LinkStack")]
[assembly: System.Reflection.AssemblyDescription("Łączenie przepustowości wielu łączy przez równoległe połączenia")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.2.0")]
namespace LinkStack {
    static class Program {
        [STAThread] static void Main(string[] args) {
            if (args.Length == 2 && args[0] == "--watch-proxy") { SystemProxy.Watch(int.Parse(args[1])); return; }
            bool owner;
            using (var mutex = new Mutex(true, @"Local\LinkStack.GUI", out owner)) {
                if (!owner && (Array.IndexOf(args, "--steam-ui") >= 0 || Array.IndexOf(args, "--steam-start") >= 0)) { Thread.Sleep(1500); owner = mutex.WaitOne(3000); }
                if (!owner) { MessageBox.Show("LinkStack jest już uruchomiony."); return; }
                try {
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    try { SystemProxy.Recover(false); } catch (Exception e) { MessageBox.Show("Nie udało się przywrócić wcześniejszego proxy: " + e.Message); }
                    int handoff = Array.IndexOf(args, "--steam-start");
                    Application.Run(new MainForm(Array.IndexOf(args, "--steam-ui") >= 0 || handoff >= 0, handoff >= 0,
                        handoff >= 0 && args.Length > handoff + 1 ? args[handoff + 1] : null, Array.IndexOf(args, "--with-system-proxy") >= 0));
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
