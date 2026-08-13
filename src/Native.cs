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
        public const int OFFSCREEN_XY = -32000;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
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
