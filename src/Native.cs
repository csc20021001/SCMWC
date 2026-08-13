using System;
using System.Runtime.InteropServices;

namespace ScreenWatch
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint c1, c2, c3;
    }

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd,
                                      int idObject, int idChild, uint thread, uint time);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    public static class Native
    {
        // PrintWindow 一定要帶這個 flag，不然硬體合成的視窗（Chromium / Electron）
        // 會回傳 100% 黑畫面。這是探針實測出來的，不是選項。
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        public const int SW_RESTORE = 9;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const int OFFSCREEN_XY = -32000;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const int OBJID_WINDOW = 0;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        public const int DIB_RGB_COLORS = 0;
        public const uint BI_RGB = 0;

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        public const int GA_ROOT = 2;

        /// <summary>
        /// 座標底下最上層的「真正的」視窗。
        ///
        /// 不能用 WindowFromPoint：它會打到「工作切換」那類使用者看不見的
        /// TOPMOST 系統視窗，也會打到呼叫者自己的遮罩。改成照 z 序列舉，
        /// 濾掉隱形（cloaked）、最小化、尺寸過小、整個桌面大小的殼層視窗。
        /// </summary>
        public static IntPtr TopWindowAt(int sx, int sy, IntPtr exclude, bool skipOwnProcess)
        {
            IntPtr result = IntPtr.Zero;
            System.Drawing.Rectangle virt = System.Windows.Forms.SystemInformation.VirtualScreen;
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            EnumWindowsProc cb = delegate(IntPtr h, IntPtr p)
            {
                if (result != IntPtr.Zero) return false;      // EnumWindows 是由上而下
                if (h == exclude) return true;
                if (skipOwnProcess)
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid == self) return true;
                }
                if (!IsWindowVisible(h)) return true;
                if (IsIconic(h)) return true;
                if (IsCloaked(h)) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                if (r.Width < 40 || r.Height < 40) return true;
                if (r.Width >= virt.Width && r.Height >= virt.Height) return true;
                if (sx < r.Left || sx >= r.Right || sy < r.Top || sy >= r.Bottom) return true;
                result = h;
                return false;
            };
            EnumWindows(cb, IntPtr.Zero);
            return result;
        }

        /// <summary>
        /// 這個視窗在指定的矩形範圍裡，現在是不是真的看得見（沒有被別的視窗蓋住）。
        ///
        /// 取幾個點做命中測試：只要有一個點打到目標本身，就算看得見。
        /// 沒有這個判斷的話，浮動關閉鈕會一直蓋在使用者正在用的程式上面。
        /// </summary>
        public static bool IsVisibleAt(IntPtr hwnd, System.Drawing.Rectangle region)
        {
            if (region.Width < 2 || region.Height < 2) return false;
            int insetX = Math.Max(1, region.Width / 8);
            int insetY = Math.Max(1, region.Height / 8);
            var pts = new POINT[]
            {
                new POINT(region.Left + region.Width / 2, region.Top + region.Height / 2),
                new POINT(region.Left + insetX, region.Top + insetY),
                new POINT(region.Right - insetX, region.Top + insetY),
                new POINT(region.Left + insetX, region.Bottom - insetY),
                new POINT(region.Right - insetX, region.Bottom - insetY),
            };
            // 浮動關閉鈕本身只有 32x32，會被上面的尺寸下限濾掉，不會擋到自己。
            for (int i = 0; i < pts.Length; i++)
                if (TopWindowAt(pts[i].X, pts[i].Y, IntPtr.Zero, false) == hwnd) return true;
            return false;
        }
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
            int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hwnd, System.Text.StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max,
            IntPtr hmodWinEventProc, WinEventProc cb, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);

        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,
            out uint pid);

        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type,
            out uint dpiX, out uint dpiY);

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd,
            uint attr, out int value, int size);

        public const uint DWMWA_CLOAKED = 14;
        public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd,
            uint attr, ref int value, int size);

        /// <summary>讓標題列跟著深色主題走，不然亮色標題列配深色內容很突兀。</summary>
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                // 20 是 Win10 2004 之後的值；更舊的版本用 19。兩個都試。
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
                // 不強制重畫非工作區的話，標題列要等下一次改變大小才會變色
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 視窗是否被 DWM 隱形。Win11 上很多 UWP 視窗一直存在但是 cloaked，
        /// 挑選目標時如果不濾掉，使用者會選到一個看不見的視窗。
        /// </summary>
        public static bool IsCloaked(IntPtr hwnd)
        {
            int v;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out v, 4) != 0) return false;
            return v != 0;
        }

        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

        /// <summary>
        /// 提高系統計時器精度。預設是 15.6ms，Thread.Sleep(1) 實際會睡 15ms，
        /// 高頻取樣會直接被這個拖垮（設定每秒 20 次只跑得到 15 次）。
        /// </summary>
        public static void BeginHighResolutionTimer() { try { timeBeginPeriod(1); } catch (Exception) { } }
        public static void EndHighResolutionTimer() { try { timeEndPeriod(1); } catch (Exception) { } }

        public static double SystemScale()
        {
            try
            {
                uint d = GetDpiForSystem();
                if (d >= 96) return d / 96.0;
            }
            catch (Exception) { }
            return 1.0;
        }

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp,
            uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bmi, uint usage);

        public static void MakeDpiAware()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;  // PER_MONITOR_AWARE_V2
            }
            catch (Exception) { }
            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        /// <summary>目標視窗自己的 DPI 與所在螢幕的 DPI。兩者不同代表目標是 DPI-unaware。</summary>
        public static void GetDpis(IntPtr hwnd, out uint windowDpi, out uint monitorDpi)
        {
            windowDpi = 96;
            monitorDpi = 96;
            try { uint d = GetDpiForWindow(hwnd); if (d != 0) windowDpi = d; }
            catch (Exception) { }
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, 2);  // MONITOR_DEFAULTTONEAREST
                uint x, y;
                if (GetDpiForMonitor(mon, 0, out x, out y) == 0 && x != 0) monitorDpi = x;
            }
            catch (Exception) { }
        }

        public static string TitleOf(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(512);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
