using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>
    /// 不經過滑鼠的測試入口。UI 的座標運算和事件接線最容易寫錯，
    /// 這些測試走的是跟真實操作相同的程式路徑。
    /// </summary>
    public static class SelfTest
    {
        static string LogPath(string name)
        {
            return Path.Combine(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location), name);
        }

        static void Write(string name, StringBuilder sb)
        {
            File.WriteAllText(LogPath(name), sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 拿產品的 Capturer + Watch 去盯一個真實視窗。
        /// 用法：ScreenWatch.exe --selftest "視窗標題片段"
        /// </summary>
        public static void Run(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到標題含 " + titlePart + " 的視窗");
                Write("selftest.log", sb);
                return;
            }

            RECT r;
            Native.GetWindowRect(hwnd, out r);
            uint wdpi, mdpi;
            Native.GetDpis(hwnd, out wdpi, out mdpi);
            sb.AppendLine(string.Format("目標 : {0}", Native.TitleOf(hwnd)));
            sb.AppendLine(string.Format("視窗 : {0}x{1} @ ({2},{3})  DPI 視窗={4} 螢幕={5}",
                r.Width, r.Height, r.Left, r.Top, wdpi, mdpi));

            Rectangle screenRegion = new Rectangle(
                r.Left + r.Width / 8, r.Top + r.Height / 25,
                r.Width * 3 / 4, r.Height / 3);
            sb.AppendLine(string.Format("區域 : {0}", screenRegion));

            int fires = 0;
            using (var w = new Watch(hwnd, screenRegion))
            {
                w.Trig.ChangeThreshold = 0.5;
                w.NotifyCooldownSeconds = 0;
                w.IntervalSeconds = 0;
                w.Triggered += delegate(Watch _, string reason, Bitmap bmp)
                {
                    fires++;
                    if (bmp != null)
                    {
                        if (fires == 1)
                            bmp.Save(LogPath("selftest-shot.png"),
                                     System.Drawing.Imaging.ImageFormat.Png);
                        bmp.Dispose();
                    }
                };

                sb.AppendLine();
                sb.AppendLine("  #   status      delta   detail");
                for (int i = 0; i < 20; i++)
                {
                    w.Tick(0.5, true);
                    sb.AppendLine(string.Format(" {0,2}   {1,-10}  {2,6:0.00}  {3}",
                        i, w.Status, w.LastDelta, w.StatusDetail));
                    System.Threading.Thread.Sleep(500);
                }
                sb.AppendLine();
                sb.AppendLine(string.Format("通知次數  : {0}", fires));
                sb.AppendLine(string.Format("覆蓋率    : live {0:0.0}s / blind {1:0.0}s",
                    w.LiveSeconds, w.BlindSeconds));
                sb.AppendLine(string.Format("結論      : {0}",
                    fires > 0 && w.BlindSeconds == 0 ? "PASS" : "FAIL"));
            }
            Write("selftest.log", sb);
        }

        /// <summary>
        /// 驗證四種通知條件。用法：ScreenWatch.exe --triggertest "視窗標題片段"
        /// 拿 ProgressDemo 當目標最合適：它會跑進度條，也會在完成時整條變綠。
        /// </summary>
        public static void TriggerTest(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到視窗：" + titlePart);
                Write("triggertest.log", sb);
                return;
            }

            RECT r;
            Native.GetWindowRect(hwnd, out r);
            sb.AppendLine(string.Format("目標 : {0}  {1}x{2}",
                Native.TitleOf(hwnd), r.Width, r.Height));
            sb.AppendLine();

            // ProgressDemo 的版面：上方橫幅、百分比、進度條。
            // 進度條在視窗高度 55%~62% 的位置（由 --bartest 量出來的）。
            Rectangle bar = new Rectangle(r.Left + (int)(r.Width * 0.02),
                                          r.Top + (int)(r.Height * 0.55),
                                          (int)(r.Width * 0.96), (int)(r.Height * 0.08));
            Rectangle banner = new Rectangle(r.Left + (int)(r.Width * 0.05),
                                             r.Top + (int)(r.Height * 0.10),
                                             (int)(r.Width * 0.90), (int)(r.Height * 0.14));

            var kinds = new TriggerKind[] { TriggerKind.AnyChange, TriggerKind.Full,
                                            TriggerKind.PercentReached, TriggerKind.ColorChanged };
            foreach (TriggerKind kind in kinds)
            {
                bool useBanner = kind == TriggerKind.ColorChanged;
                using (var w = new Watch(hwnd, useBanner ? banner : bar))
                {
                    w.Trig.Kind = kind;
                    w.Trig.ChangeThreshold = 1.0;
                    w.Trig.PercentTarget = 50;
                    w.Trig.ColorTolerance = 45;
                    w.IntervalSeconds = 0;
                    w.NotifyCooldownSeconds = 0;

                    int fires = 0;
                    string firstReason = "";
                    w.Triggered += delegate(Watch _, string reason, Bitmap bmp)
                    {
                        fires++;
                        if (firstReason.Length == 0) firstReason = reason;
                        if (bmp != null) bmp.Dispose();
                    };

                    double maxPct = -1;
                    for (int i = 0; i < 70; i++)      // 約 35 秒，夠跑完一輪 12 秒的循環
                    {
                        w.Tick(0.5, true);
                        if (w.Trig.LastPercent > maxPct) maxPct = w.Trig.LastPercent;
                        System.Threading.Thread.Sleep(500);
                    }
                    sb.AppendLine(string.Format("{0,-15} 觸發 {1,3} 次   量到的最高進度 {2,5:0}%   {3}",
                        kind, fires, maxPct, firstReason));
                    sb.AppendLine(string.Format("{0,-15} 條件說明：{1}", "", w.Trig.Describe()));
                }
            }
            Write("triggertest.log", sb);
        }

        /// <summary>
        /// 量一次取樣實際花多久、瓶頸在哪一段，據此決定抓取頻率的上限。
        /// 用法：ScreenWatch.exe --bench "視窗標題片段"
        /// </summary>
        public static void Bench(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            sb.AppendLine("視窗                              尺寸        PrintWindow  讀回像素  分析   合計   最快頻率");
            sb.AppendLine(new string('-', 100));

            foreach (string part in titlePart.Split('|'))
            {
                IntPtr hwnd = FindWindow(part.Trim());
                if (hwnd == IntPtr.Zero)
                {
                    sb.AppendLine(string.Format("{0,-32}  找不到", part.Trim()));
                    continue;
                }
                RECT r;
                Native.GetWindowRect(hwnd, out r);
                var region = new Rectangle(0, 0, r.Width, r.Height);

                using (var cap = new Capturer(r.Width, r.Height))
                {
                    double print = 0, read = 0, analyse = 0;
                    const int N = 30;
                    for (int i = 0; i < N; i++)
                    {
                        cap.Grab(hwnd);
                        print += cap.LastPrintMs;
                        read += cap.LastReadMs;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        cap.Analyse(region, true);
                        analyse += sw.Elapsed.TotalMilliseconds;
                    }
                    print /= N; read /= N; analyse /= N;
                    double total = print + read + analyse;
                    string name = Native.TitleOf(hwnd);
                    if (name.Length > 30) name = name.Substring(0, 30);
                    sb.AppendLine(string.Format(
                        "{0,-32} {1,5}x{2,-5} {3,9:0.0}ms {4,8:0.0}ms {5,6:0.0}ms {6,6:0.0}ms {7,7:0} Hz",
                        name, r.Width, r.Height, print, read, analyse, total,
                        total > 0 ? 1000.0 / total : 0));
                }
            }
            Write("bench.log", sb);
        }

        /// <summary>
        /// 逐筆記錄每次取樣量到的進度，用來看清楚「為什麼沒觸發」。
        /// 條件跟產品預設完全一樣：整個視窗、跑完了才通知、每秒 20 次。
        /// 用法：ScreenWatch.exe --trace "視窗標題片段"
        /// </summary>
        public static void Trace(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            Native.BeginHighResolutionTimer();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到視窗：" + titlePart);
                Write("trace.log", sb);
                return;
            }
            RECT r;
            Native.GetWindowRect(hwnd, out r);

            int fires = 0;
            double maxPct = -1;
            var series = new List<string>();
            using (var w = new Watch(hwnd, new Rectangle(r.Left, r.Top, r.Width, r.Height)))
            {
                w.Trig.Kind = TriggerKind.Full;
                w.IntervalSeconds = 0.05;
                w.Triggered += delegate(Watch _, string reason, Bitmap bmp)
                {
                    fires++;
                    series.Add("        *** 觸發：" + reason);
                    if (bmp != null) bmp.Dispose();
                };

                DateTime t0 = DateTime.UtcNow;
                int lastCount = 0;
                while ((DateTime.UtcNow - t0).TotalSeconds < 20)
                {
                    w.Tick(0, false);
                    if (w.SampleCount != lastCount)
                    {
                        lastCount = w.SampleCount;
                        if (w.Trig.LastPercent > maxPct) maxPct = w.Trig.LastPercent;
                        if (series.Count < 400)
                            series.Add(string.Format("{0,6:0.00}s  進度 {1,6:0.0}%",
                                (DateTime.UtcNow - t0).TotalSeconds, w.Trig.LastPercent));
                    }
                    double wait = w.MsUntilDue;
                    System.Threading.Thread.Sleep((int)Math.Max(1, Math.Min(15, wait)));
                }
                sb.AppendLine("目標 : " + Native.TitleOf(hwnd));
                sb.AppendLine("找到的進度條位置 : " + w.Trig.BandInfo);
                sb.AppendLine(string.Format("20 秒取樣 {0} 次，量到的最高進度 {1:0.0}%，觸發 {2} 次",
                    w.SampleCount, maxPct, fires));
                sb.AppendLine();
                foreach (string s in series) sb.AppendLine(s);
            }
            Native.EndHighResolutionTimer();
            Write("trace.log", sb);
        }

        /// <summary>
        /// 量「實際達到」的取樣頻率，而不是設定值。
        /// 同時測 1 項和 3 項監視，看多項並存時會掉多少。
        /// 用法：ScreenWatch.exe --ratetest "視窗標題片段"
        /// </summary>
        public static void RateTest(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到視窗：" + titlePart);
                Write("ratetest.log", sb);
                return;
            }
            RECT r;
            Native.GetWindowRect(hwnd, out r);
            var whole = new Rectangle(r.Left, r.Top, r.Width, r.Height);

            Native.BeginHighResolutionTimer();   // 產品裡也會開，測試條件要一致
            sb.AppendLine("設定值      監視項目數   實際達到       每項");
            sb.AppendLine(new string('-', 56));

            double[] settings = new double[] { 0.05, 0.2, 1.0 };
            int[] counts = new int[] { 1, 3 };

            foreach (double interval in settings)
            {
                foreach (int n in counts)
                {
                    var list = new List<Watch>();
                    for (int i = 0; i < n; i++)
                    {
                        var w = new Watch(hwnd, whole);
                        w.IntervalSeconds = interval;
                        list.Add(w);
                    }
                    // 跟產品的取樣迴圈一樣的作法
                    DateTime until = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < until)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            try { list[i].Tick(0, false); }
                            catch (Exception) { }
                        }
                        double wait = 15;
                        for (int i = 0; i < list.Count; i++)
                            if (list[i].MsUntilDue < wait) wait = list[i].MsUntilDue;
                        System.Threading.Thread.Sleep((int)Math.Max(1, Math.Min(15, wait)));
                    }
                    int total = 0;
                    foreach (Watch w in list) { total += w.SampleCount; w.Dispose(); }
                    sb.AppendLine(string.Format("每 {0,4:0.00} 秒 {1,6} 項     {2,6:0.0} 次/秒   {3,5:0.0} 次/秒",
                        interval, n, total / 5.0, total / 5.0 / n));
                }
            }
            Native.EndHighResolutionTimer();
            Write("ratetest.log", sb);
        }

        /// <summary>
        /// 診斷「跑完了才通知」在不同框選方式下會發生什麼。
        /// 使用者最自然的操作是「點一下選整個視窗」，這個測試就是要驗證那樣也能work。
        /// 用法：ScreenWatch.exe --bartest "視窗標題片段"
        /// </summary>
        public static void BarTest(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到視窗：" + titlePart);
                Write("bartest.log", sb);
                return;
            }

            RECT r;
            Native.GetWindowRect(hwnd, out r);
            sb.AppendLine(string.Format("目標 : {0}  {1}x{2}",
                Native.TitleOf(hwnd), r.Width, r.Height));
            sb.AppendLine();

            var cases = new[] {
                new { Name = "整個視窗（點一下選的）",
                      R = new Rectangle(r.Left, r.Top, r.Width, r.Height) },
                // 進度條實際位置由前面兩個案例回推得到：視窗高度的 55%~62%
                new { Name = "只框進度條那一條",
                      R = new Rectangle(r.Left + (int)(r.Width * 0.02),
                                        r.Top + (int)(r.Height * 0.55),
                                        (int)(r.Width * 0.96), (int)(r.Height * 0.08)) },
                new { Name = "框下半部（含進度條和按鈕）",
                      R = new Rectangle(r.Left, r.Top + r.Height / 2,
                                        r.Width, r.Height / 2) },
            };

            foreach (var c in cases)
            {
                using (var w = new Watch(hwnd, c.R))
                {
                    w.Trig.Kind = TriggerKind.Full;
                    w.IntervalSeconds = 0;
                    w.NotifyCooldownSeconds = 0;
                    int fires = 0;
                    string first = "";
                    w.Triggered += delegate(Watch _, string reason, Bitmap bmp)
                    {
                        fires++;
                        if (first.Length == 0) first = reason;
                        if (bmp != null) bmp.Dispose();
                    };

                    double maxPct = -1;
                    int measured = 0;
                    for (int i = 0; i < 90; i++)          // 約 27 秒，涵蓋兩輪
                    {
                        w.Tick(0.3, true);
                        if (w.Trig.LastPercent >= 0) { measured++; }
                        if (w.Trig.LastPercent > maxPct) maxPct = w.Trig.LastPercent;
                        System.Threading.Thread.Sleep(300);
                    }
                    sb.AppendLine(string.Format("{0,-24} 量得到 {1,3}/90   最高 {2,4:0}%   觸發 {3} 次   找到的位置：{4}",
                        c.Name, measured, maxPct, fires, w.Trig.BandInfo));
                    if (first.Length > 0) sb.AppendLine(new string(' ', 26) + first);
                }
            }
            Write("bartest.log", sb);
        }

        /// <summary>
        /// 迴歸測試：進度條滿了立刻歸零時，「跑完了才通知」還抓不抓得到。
        /// 刻意用很慢的取樣（每 3 秒）盯一個 5 秒跑完就歸零的進度條 ——
        /// 那個 100% 幾乎不可能剛好被取樣到，必須靠「進度突然倒退」推斷出來。
        /// 用法：ScreenWatch.exe --misstest "視窗標題片段"
        /// </summary>
        public static void MissTest(string titlePart)
        {
            var sb = new StringBuilder();
            Native.MakeDpiAware();
            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到視窗：" + titlePart);
                Write("misstest.log", sb);
                return;
            }

            RECT r;
            Native.GetWindowRect(hwnd, out r);
            Rectangle bar = new Rectangle(r.Left + (int)(r.Width * 0.02),
                                          r.Top + (int)(r.Height * 0.55),
                                          (int)(r.Width * 0.96), (int)(r.Height * 0.08));
            sb.AppendLine("目標 : " + Native.TitleOf(hwnd));
            sb.AppendLine("情境 : 進度條 5 秒跑完立刻歸零，監視每 3 秒才看一次");
            sb.AppendLine();

            int fires = 0;
            var reasons = new List<string>();
            using (var w = new Watch(hwnd, bar))
            {
                w.Trig.Kind = TriggerKind.Full;
                w.IntervalSeconds = 3.0;
                w.NotifyCooldownSeconds = 10;   // 刻意留著，驗證冷卻不會吃掉邊緣觸發
                w.Triggered += delegate(Watch _, string reason, Bitmap bmp)
                {
                    fires++;
                    if (reasons.Count < 6) reasons.Add(reason);
                    if (bmp != null) bmp.Dispose();
                };

                DateTime until = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < until)
                {
                    w.Tick(0.1, false);         // 用真正的間隔判斷，不強制取樣
                    System.Threading.Thread.Sleep(100);
                }
            }

            sb.AppendLine(string.Format("45 秒內觸發 {0} 次（進度條約跑完 9 輪）", fires));
            foreach (string s in reasons) sb.AppendLine("  → " + s);
            sb.AppendLine();
            sb.AppendLine("結論 : " + (fires >= 3 ? "PASS" : "FAIL"));
            Write("misstest.log", sb);
        }

        /// <summary>
        /// 驗證「點某一列的叉叉就移除」，包含座標命中判定與事件接線。
        /// 用法：ScreenWatch.exe --uitest "目標視窗標題片段"
        /// </summary>
        public static void UiTest(string titlePart)
        {
            Native.MakeDpiAware();
            var sb = new StringBuilder();

            IntPtr hwnd = FindWindow(titlePart);
            if (hwnd == IntPtr.Zero)
            {
                sb.AppendLine("找不到目標視窗：" + titlePart);
                Write("uitest.log", sb);
                return;
            }

            RECT r;
            Native.GetWindowRect(hwnd, out r);
            var watches = new List<Watch>();
            // 同時放兩項，順便驗證多項監視
            watches.Add(new Watch(hwnd, new Rectangle(r.Left, r.Top, r.Width, r.Height)));
            watches.Add(new Watch(hwnd, new Rectangle(r.Left + 10, r.Top + 10,
                                                     r.Width / 2, r.Height / 2)));

            using (var form = new MainForm())
            {
                var removed = new List<Watch>();
                form.RemoveRequested += delegate(Watch x)
                {
                    removed.Add(x);
                    watches.Remove(x);
                    form.RefreshList(watches);
                };

                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-32000, -32000);   // 測試時不要擋畫面
                form.Show();
                form.RefreshList(watches);

                sb.AppendLine(string.Format("初始列數           : {0}（預期 2）", form.RowCount));

                for (int i = 0; i < 20; i++)
                {
                    watches[0].Tick(0.5, true);
                    form.RefreshList(watches);
                }
                sb.AppendLine(string.Format("20 次更新後列數     : {0}（預期 2）", form.RowCount));

                bool hit1 = form.TestClickClose(0);
                sb.AppendLine(string.Format("點第 1 列的叉叉     : {0}  剩下 {1} 項",
                    hit1 ? "命中" : "沒命中", watches.Count));

                bool hit2 = form.TestClickClose(0);
                sb.AppendLine(string.Format("再點一次            : {0}  剩下 {1} 項",
                    hit2 ? "命中" : "沒命中", watches.Count));

                sb.AppendLine(string.Format("空清單時點叉叉     : {0}（預期沒命中）",
                    form.TestClickClose(0) ? "命中" : "沒命中"));

                sb.AppendLine();
                sb.AppendLine(string.Format("結論 : {0}",
                    hit1 && hit2 && watches.Count == 0 && removed.Count == 2 ? "PASS" : "FAIL"));
                form.Hide();
            }
            for (int i = 0; i < watches.Count; i++) watches[i].Dispose();
            Write("uitest.log", sb);
        }

        /// <summary>
        /// 送一則測試推播。用法：ScreenWatch.exe --discordtest "https://..."
        /// </summary>
        public static void DiscordTest(string url) { DiscordTest(url, false); }

        public static void DiscordTest(string url, bool withImage)
        {
            var sb = new StringBuilder();
            Config.Load();
            Config.DiscordWebhook = url;
            Config.DiscordAttachImage = withImage;
            sb.AppendLine("webhook = " + url);
            sb.AppendLine("附圖 = " + withImage);
            sb.AppendLine("Configured = " + Discord.Configured);
            if (!Discord.Configured)
            {
                Write("discordtest.log", sb);
                return;
            }
            Bitmap img = null;
            if (withImage)
            {
                img = new Bitmap(120, 40);
                using (var g = Graphics.FromImage(img))
                {
                    g.Clear(Color.FromArgb(40, 160, 90));
                    g.DrawString("Completed", new Font("Segoe UI", 12), Brushes.White, 4, 8);
                }
            }
            Discord.LastResult = "送出中…";
            Discord.Send("ScreenWatch 測試推播：**Progress Demo** 跑完了（100%）", img);
            for (int i = 0; i < 40 && Discord.LastResult == "送出中…"; i++)
                System.Threading.Thread.Sleep(250);
            sb.AppendLine("結果 = " + Discord.LastResult);
            Write("discordtest.log", sb);
        }

        /// <summary>
        /// 印出某座標會選到哪個視窗。用法：ScreenWatch.exe --pick x,y
        /// </summary>
        public static void Pick(string coords)
        {
            Native.MakeDpiAware();
            string[] parts = coords.Split(',');
            int x = int.Parse(parts[0].Trim());
            int y = int.Parse(parts[1].Trim());

            IntPtr h = RegionSelector.TopWindowAt(x, y, IntPtr.Zero);
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("座標 ({0},{1})", x, y));
            if (h == IntPtr.Zero)
            {
                sb.AppendLine("→ 沒有選到任何視窗");
            }
            else
            {
                RECT r;
                Native.GetWindowRect(h, out r);
                sb.AppendLine(string.Format("→ hwnd={0}  {1}x{2} @ ({3},{4})",
                    h.ToInt64(), r.Width, r.Height, r.Left, r.Top));
                sb.AppendLine(string.Format("→ 標題 {0}", Native.TitleOf(h)));
                sb.AppendLine(string.Format("→ 座標在範圍內={0}  cloaked={1}  iconic={2}",
                    x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom,
                    Native.IsCloaked(h), Native.IsIconic(h)));
            }
            Write("pick.log", sb);
        }

        public static IntPtr FindWindow(string part)
        {
            IntPtr found = IntPtr.Zero;
            string needle = part.ToLowerInvariant();
            EnumWindowsProc cb = delegate(IntPtr h, IntPtr p)
            {
                if (found != IntPtr.Zero) return false;
                if (!Native.IsWindowVisible(h)) return true;
                string t = Native.TitleOf(h);
                if (t.Length > 0 && t.ToLowerInvariant().Contains(needle)) found = h;
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            return found;
        }
    }
}
