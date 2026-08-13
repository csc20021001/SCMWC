using System;
using System.Drawing;

namespace ScreenWatch
{
    /// <summary>一次取樣的結果：畫面簽章 + 兩個健康指標。</summary>
    public sealed class Sample
    {
        public byte[] Signature;
        public double Unpainted;   // 完全沒被畫到的比例（哨兵色還在）
        public double Black;       // 純黑比例
        public bool CallSucceeded;

        /// <summary>抓到的內容有沒有意義。沒有意義時不能拿來比較，也不能算成「沒變化」。</summary>
        public bool IsUsable
        {
            get { return CallSucceeded && Unpainted < 0.5 && Black < 0.9; }
        }
    }

    /// <summary>
    /// 抓取核心。抓取前先把畫布塗成哨兵洋紅，這樣「呼叫成功但什麼都沒畫」
    /// 才不會被誤認成「畫面全黑」或「畫面沒變」。
    /// </summary>
    public sealed class Capturer : IDisposable
    {
        public const int Grid = 64;                 // 簽章網格邊長
        const uint Sentinel = 0x00FF00FF;           // COLORREF 洋紅

        IntPtr hdcScreen, hdcMem, hbmp, brush;
        int w, h;
        byte[] buf;
        BITMAPINFO bmi;

        public Capturer(int width, int height)
        {
            w = Math.Max(1, width);
            h = Math.Max(1, height);
            hdcScreen = Native.GetDC(IntPtr.Zero);
            hdcMem = Native.CreateCompatibleDC(hdcScreen);
            hbmp = Native.CreateCompatibleBitmap(hdcScreen, w, h);
            brush = Native.CreateSolidBrush(Sentinel);
            buf = new byte[w * h * 4];
            bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;            // 負值 = top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = Native.BI_RGB;
        }

        public void Dispose()
        {
            if (brush != IntPtr.Zero) { Native.DeleteObject(brush); brush = IntPtr.Zero; }
            if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
            if (hdcMem != IntPtr.Zero) { Native.DeleteDC(hdcMem); hdcMem = IntPtr.Zero; }
            if (hdcScreen != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); hdcScreen = IntPtr.Zero; }
        }

        // 上一次抓取各階段花的毫秒數，用來找瓶頸
        public double LastPrintMs;
        public double LastReadMs;

        /// <summary>抓一張整個視窗的畫面到內部緩衝區。</summary>
        public bool Grab(IntPtr hwnd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            IntPtr old = Native.SelectObject(hdcMem, hbmp);
            bool ok;
            try
            {
                RECT full = new RECT();
                full.Right = w;
                full.Bottom = h;
                Native.FillRect(hdcMem, ref full, brush);
                ok = Native.PrintWindow(hwnd, hdcMem, Native.PW_RENDERFULLCONTENT);
            }
            finally
            {
                Native.SelectObject(hdcMem, old);   // GetDIBits 要求 bitmap 不能還選在 DC 上
            }
            LastPrintMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            bool read = Native.GetDIBits(hdcMem, hbmp, 0, (uint)h, buf, ref bmi,
                                         Native.DIB_RGB_COLORS) != 0;
            LastReadMs = sw.Elapsed.TotalMilliseconds;
            return ok && read;
        }

        /// <summary>從已抓取的緩衝區裡，對指定區域算出簽章與指標。</summary>
        public Sample Analyse(Rectangle region, bool callSucceeded)
        {
            int rx = Clamp(region.X, 0, w - 1);
            int ry = Clamp(region.Y, 0, h - 1);
            int rw = Clamp(region.Width, 1, w - rx);
            int rh = Clamp(region.Height, 1, h - ry);

            byte[] sig = new byte[Grid * Grid];
            int unpainted = 0, black = 0, i = 0;
            for (int gy = 0; gy < Grid; gy++)
            {
                int y = ry + (gy * rh) / Grid;
                if (y >= h) y = h - 1;
                int row = y * w * 4;
                for (int gx = 0; gx < Grid; gx++)
                {
                    int x = rx + (gx * rw) / Grid;
                    if (x >= w) x = w - 1;
                    int p = row + x * 4;
                    int b = buf[p], g = buf[p + 1], r = buf[p + 2];
                    if (r == 0xFF && g == 0x00 && b == 0xFF) unpainted++;
                    int luma = (r * 299 + g * 587 + b * 114) / 1000;
                    if (luma < 8) black++;
                    sig[i++] = (byte)luma;
                }
            }
            int n = Grid * Grid;
            Sample s = new Sample();
            s.Signature = sig;
            s.Unpainted = (double)unpainted / n;
            s.Black = (double)black / n;
            s.CallSucceeded = callSucceeded;
            return s;
        }

        /// <summary>被實際畫到的範圍。用來校正 DPI-unaware 目標交回來的較小影像。</summary>
        public Rectangle PaintedBounds()
        {
            int minx = w, miny = h, maxx = -1, maxy = -1;
            for (int y = 0; y < h; y += 2)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x += 2)
                {
                    int p = row + x * 4;
                    if (!(buf[p] == 0xFF && buf[p + 1] == 0x00 && buf[p + 2] == 0xFF))
                    {
                        if (x < minx) minx = x;
                        if (x > maxx) maxx = x;
                        if (y < miny) miny = y;
                        if (y > maxy) maxy = y;
                    }
                }
            }
            if (maxx < 0) return Rectangle.Empty;
            return new Rectangle(minx, miny, maxx - minx + 1, maxy - miny + 1);
        }

        public Bitmap ToBitmap(Rectangle region)
        {
            int rx = Clamp(region.X, 0, w - 1);
            int ry = Clamp(region.Y, 0, h - 1);
            int rw = Clamp(region.Width, 1, w - rx);
            int rh = Clamp(region.Height, 1, h - ry);
            Bitmap bmp = new Bitmap(rw, rh, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, rw, rh),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                for (int y = 0; y < rh; y++)
                {
                    IntPtr dst = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(
                        buf, ((ry + y) * w + rx) * 4, dst, rw * 4);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        /// <summary>區域的平均顏色。用來判斷「變色了」。</summary>
        public Color AverageColor(Rectangle region)
        {
            int rx = Clamp(region.X, 0, w - 1);
            int ry = Clamp(region.Y, 0, h - 1);
            int rw = Clamp(region.Width, 1, w - rx);
            int rh = Clamp(region.Height, 1, h - ry);
            long sr = 0, sg = 0, sb = 0;
            int n = 0;
            int stepY = Math.Max(1, rh / 32);
            int stepX = Math.Max(1, rw / 32);
            for (int y = ry; y < ry + rh; y += stepY)
            {
                int row = y * w * 4;
                for (int x = rx; x < rx + rw; x += stepX)
                {
                    int p = row + x * 4;
                    sb += buf[p]; sg += buf[p + 1]; sr += buf[p + 2];
                    n++;
                }
            }
            if (n == 0) return Color.Black;
            return Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n));
        }

        /// <summary>
        /// 把區域切成 n 個直欄，回傳每欄的平均顏色。
        /// 只取中間 50% 的高度，避開進度條的外框與陰影。
        /// </summary>
        public Color[] ColumnColors(Rectangle region, int n)
        {
            int rx = Clamp(region.X, 0, w - 1);
            int ry = Clamp(region.Y, 0, h - 1);
            int rw = Clamp(region.Width, 1, w - rx);
            int rh = Clamp(region.Height, 1, h - ry);
            int y0 = ry + rh / 4;
            int y1 = ry + rh - rh / 4;
            if (y1 <= y0) y1 = y0 + 1;

            var cols = new Color[n];
            for (int i = 0; i < n; i++)
            {
                int xa = rx + (int)((long)i * rw / n);
                int xb = rx + (int)((long)(i + 1) * rw / n);
                if (xb <= xa) xb = xa + 1;
                if (xb > rx + rw) xb = rx + rw;
                long sr = 0, sg = 0, sb = 0;
                int cnt = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w * 4;
                    for (int x = xa; x < xb; x++)
                    {
                        int p = row + x * 4;
                        sb += buf[p]; sg += buf[p + 1]; sr += buf[p + 2];
                        cnt++;
                    }
                }
                cols[i] = cnt == 0 ? Color.Black
                    : Color.FromArgb((int)(sr / cnt), (int)(sg / cnt), (int)(sb / cnt));
            }
            return cols;
        }

        public static double ColorDistance(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>兩張簽章的平均絕對差（0~255）。</summary>
        public static double Delta(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            long sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
            return (double)sum / a.Length;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
