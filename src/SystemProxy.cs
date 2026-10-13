using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace LinkStack {
    public static class SystemProxy {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        const string Ours = "http=127.0.0.1:17881;https=127.0.0.1:17881";
        static readonly string State = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkStack", "proxy-backup.xml");
        [DllImport("wininet.dll", SetLastError = true)] static extern bool InternetSetOption(IntPtr h, int option, IntPtr buffer, int size);
        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)] static extern bool InternetQueryOption(IntPtr h, int option, IntPtr buffer, ref int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)] static extern bool SetConnectionOption(IntPtr h, int option, IntPtr buffer, int size);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr pointer);
        [StructLayout(LayoutKind.Sequential)] struct PerOption { public int Option; public IntPtr Value; }
        [StructLayout(LayoutKind.Sequential)] struct PerList { public int Size; public IntPtr Connection; public int Count, Error; public IntPtr Options; }
        sealed class NativeProxy { public int Flags; public string Server, Bypass; }
        static NativeProxy QueryNative() {
            int optionSize = Marshal.SizeOf(typeof(PerOption)), listSize = Marshal.SizeOf(typeof(PerList));
            IntPtr options = Marshal.AllocHGlobal(3 * optionSize), list = Marshal.AllocHGlobal(listSize);
            bool queried = false;
            try {
                for (int i = 0; i < 3; i++) Marshal.StructureToPtr(new PerOption {Option = i + 1, Value = IntPtr.Zero}, IntPtr.Add(options, i * optionSize), false);
                Marshal.StructureToPtr(new PerList {Size = listSize, Count = 3, Options = options}, list, false);
                int size = listSize;
                if (!InternetQueryOption(IntPtr.Zero, 75, list, ref size)) throw new IOException("Odczyt proxy WinINet: " + Marshal.GetLastWin32Error());
                queried = true;
                var flags = (PerOption)Marshal.PtrToStructure(options, typeof(PerOption));
                var server = (PerOption)Marshal.PtrToStructure(IntPtr.Add(options, optionSize), typeof(PerOption));
                var bypass = (PerOption)Marshal.PtrToStructure(IntPtr.Add(options, 2 * optionSize), typeof(PerOption));
                return new NativeProxy {Flags = flags.Value.ToInt32(), Server = server.Value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(server.Value), Bypass = bypass.Value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(bypass.Value)};
            } finally {
                if (queried) for (int i = 1; i < 3; i++) { var value = (PerOption)Marshal.PtrToStructure(IntPtr.Add(options, i * optionSize), typeof(PerOption)); if (value.Value != IntPtr.Zero) GlobalFree(value.Value); }
                Marshal.FreeHGlobal(options); Marshal.FreeHGlobal(list);
            }
        }
        static void ApplyNative(NativeProxy value) {
            int optionSize = Marshal.SizeOf(typeof(PerOption)), listSize = Marshal.SizeOf(typeof(PerList));
            IntPtr options = Marshal.AllocHGlobal(3 * optionSize), list = Marshal.AllocHGlobal(listSize);
            IntPtr server = Marshal.StringToHGlobalUni(value.Server ?? ""), bypass = Marshal.StringToHGlobalUni(value.Bypass ?? "");
            try {
                Marshal.StructureToPtr(new PerOption {Option = 1, Value = new IntPtr(value.Flags)}, options, false);
                Marshal.StructureToPtr(new PerOption {Option = 2, Value = server}, IntPtr.Add(options, optionSize), false);
                Marshal.StructureToPtr(new PerOption {Option = 3, Value = bypass}, IntPtr.Add(options, 2 * optionSize), false);
                Marshal.StructureToPtr(new PerList {Size = listSize, Count = 3, Options = options}, list, false);
                if (!SetConnectionOption(IntPtr.Zero, 75, list, listSize)) throw new IOException("Zapis proxy WinINet: " + Marshal.GetLastWin32Error());
                Refresh();
            } finally { Marshal.FreeHGlobal(server); Marshal.FreeHGlobal(bypass); Marshal.FreeHGlobal(options); Marshal.FreeHGlobal(list); }
        }
        static void Refresh() { InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); }
        internal static bool IsActive { get { var p = QueryNative(); return (p.Flags & 2) != 0 && p.Server == Ours; } }
        internal static string NativeStateForTests() { var p = QueryNative(); return p.Flags + "|" + p.Server + "|" + p.Bypass; }
        public static void Enable() {
            Recover(false);
            using (var k = Registry.CurrentUser.OpenSubKey(Key, true)) {
                if (k == null) throw new IOException("Nie znaleziono ustawień proxy Windows.");
                if (!string.IsNullOrEmpty(Convert.ToString(k.GetValue("AutoConfigURL")))) throw new InvalidOperationException("Masz konfigurację PAC. Użyj ręcznej konfiguracji proxy w aplikacji.");
                Directory.CreateDirectory(Path.GetDirectoryName(State));
                var doc = new XElement("backup", new XAttribute("pid", Process.GetCurrentProcess().Id), new XAttribute("start", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks));
                foreach (string name in new[] {"ProxyEnable", "ProxyServer", "ProxyOverride"}) {
                    object value = k.GetValue(name);
                    doc.Add(new XElement(name, new XAttribute("exists", value != null), value == null ? "" : value.ToString()));
                }
                var previous = QueryNative();
                doc.Add(new XElement("wininet", new XAttribute("flags", previous.Flags), new XElement("server", previous.Server), new XElement("bypass", previous.Bypass)));
                doc.Save(State);
                try {
                    // Watcher restores our snapshot even if the GUI crashes or is force-closed.
                    Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--watch-proxy " + Process.GetCurrentProcess().Id) {UseShellExecute = false, CreateNoWindow = true});
                    ApplyNative(new NativeProxy {Flags = 3, Server = Ours, Bypass = "<local>"});
                    var current = QueryNative();
                    if ((current.Flags & 2) == 0 || current.Server != Ours) throw new IOException("Windows nie zaakceptował ustawienia proxy.");
                } catch { Recover(true); throw; }
            }
        }
        public static void Recover(bool force) {
            if (!File.Exists(State)) {
                // RAS can hide LAN proxy settings in HKCU. Remove our orphaned
                // native proxy only when no LinkStack HTTP listener is alive.
                var orphan = QueryNative();
                if (orphan.Server == Ours && !System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(ep => ep.Port == ProxyEngine.HttpPort)) {
                    ApplyNative(new NativeProxy {Flags = (orphan.Flags & ~2) | 1, Server = "", Bypass = ""});
                }
                return;
            }
            var doc = XElement.Load(State);
            if (!force) {
                try {
                    var owner = Process.GetProcessById((int)doc.Attribute("pid"));
                    if (owner.StartTime.ToUniversalTime().Ticks == (long)doc.Attribute("start") && !owner.HasExited) return;
                } catch (ArgumentException) {} catch (InvalidOperationException) {}
            }
            using (var k = Registry.CurrentUser.OpenSubKey(Key, true)) {
                var native = doc.Element("wininet");
                if (k != null && (Convert.ToString(k.GetValue("ProxyServer")) == Ours || QueryNative().Server == Ours)) {
                    if (native != null) ApplyNative(new NativeProxy {Flags = (int)native.Attribute("flags"), Server = native.Element("server").Value, Bypass = native.Element("bypass").Value});
                    foreach (string name in new[] {"ProxyEnable", "ProxyServer", "ProxyOverride"}) {
                        var e = doc.Element(name);
                        if ((bool)e.Attribute("exists")) {
                            if (name == "ProxyEnable") k.SetValue(name, int.Parse(e.Value), RegistryValueKind.DWord);
                            else k.SetValue(name, e.Value, RegistryValueKind.String);
                        } else k.DeleteValue(name, false);
                    }
                    // ApplyNative already refreshed Windows; preserve originally absent registry values.
                    if (native == null) Refresh();
                }
            }
            File.Delete(State);
        }
        public static void Watch(int pid) {
            try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(); } catch {}
            try { Recover(false); } catch {}
        }
    }
}
