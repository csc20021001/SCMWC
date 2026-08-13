using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>
    /// 深色主控台配色。
    ///
    /// 設計稿用的是 oklch 與 Manrope / JetBrains Mono，這裡換算成最接近的 RGB，
    /// 字型退回 Windows 內建的 Segoe UI 與 Consolas ——
    /// 把字型檔一起打包會讓 92 KB 的安裝檔變成好幾 MB，不值得。
    /// </summary>
    public static class Theme
    {
        public static readonly Color Bg = FromHex("#16151A");
        public static readonly Color Line = FromHex("#2A2830");
        public static readonly Color Row = FromHex("#1E1C24");
        public static readonly Color FooterBg = FromHex("#1A1820");
        public static readonly Color LogBg = FromHex("#121116");
        public static readonly Color LogLine = FromHex("#201E26");

        public static readonly Color Text = FromHex("#F4F2EF");
        public static readonly Color TextSoft = FromHex("#C9C4D2");
        public static readonly Color TextDim = FromHex("#8B8598");
        public static readonly Color TextFaint = FromHex("#6F6A7A");
        public static readonly Color LogText = FromHex("#E6E2EA");

        public static readonly Color Accent = FromHex("#E8734A");
        public static readonly Color AccentHot = FromHex("#F08A62");
        public static readonly Color AccentInk = FromHex("#1B0F09");

        public static readonly Color OkText = FromHex("#74D69B");
        public static readonly Color OkBg = FromHex("#1E3A2A");
        public static readonly Color WarnText = FromHex("#F0A87C");
        public static readonly Color WarnBg = FromHex("#452A1E");
        public static readonly Color MutedText = FromHex("#A29CB0");
        public static readonly Color MutedBg = FromHex("#2A2830");

        public static readonly Color CloseBorder = FromHex("#3A3744");
        public static readonly Color CloseHot = FromHex("#F0705A");

        public static Color FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Rounded(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void DrawRounded(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Rounded(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }

        /// <summary>畫一個色票標籤（狀態用），回傳它佔掉的寬度。</summary>
        public static int DrawPill(Graphics g, Point at, string text, Font font,
                                   Color fore, Color back, int padX, int padY)
        {
            Size sz = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                                               TextFormatFlags.NoPadding);
            var r = new Rectangle(at.X, at.Y, sz.Width + padX * 2, sz.Height + padY * 2);
            FillRounded(g, r, Math.Max(3, r.Height / 5), back);
            TextRenderer.DrawText(g, text, font,
                new Point(r.X + padX, r.Y + padY), fore, TextFormatFlags.NoPadding);
            return r.Width;
        }
    }
}
