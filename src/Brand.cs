using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LinkStack {
    public static class Brand {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon CreateIcon() {
            using (var bitmap = new Bitmap(64, 64)) using (var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(16, 21, 30));
                using (var p = new Pen(Color.FromArgb(72, 222, 173), 7)) {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[] {new PointF(14, 18), new PointF(29, 18), new PointF(43, 32), new PointF(51, 32)});
                }
                using (var p = new Pen(Color.FromArgb(112, 160, 255), 7)) {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[] {new PointF(14, 46), new PointF(29, 46), new PointF(43, 32)});
                }
                using (var p = new Pen(Color.White, 5)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLines(p, new[] {new PointF(44, 25), new PointF(52, 32), new PointF(44, 39)}); }
                IntPtr hicon = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(hicon)) return (Icon)icon.Clone(); } finally { DestroyIcon(hicon); }
            }
        }
    }
}
