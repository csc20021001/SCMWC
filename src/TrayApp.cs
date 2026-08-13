using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace ScreenWatch
{
    public sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly Timer timer;
        readonly List<Watch> watches = new List<Watch>();
        readonly List<Badge> badges = new List<Badge>();

        // 取樣跑在背景執行緒。PrintWindow 一次要 20ms 左右，
        // 放在 UI 執行緒上只要頻率一高、項目一多，介面就會卡死。
        readonly object gate = new object();
        readonly Queue<Pending> pending = new Queue<Pending>();
        System.Threading.Thread sampler;
        volatile bool running = true;

        sealed class Pending
        {
            public Watch Watch;
            public string Reason;
            public Bitmap Shot;
            public bool IsProblem;
        }
        readonly WinEventProc minimizeProc;      // 一定要用欄位保存，不然會被 GC 回收
        readonly WinEventProc moveProc;
        IntPtr minimizeHook;
        readonly List<IntPtr> moveHooks = new List<IntPtr>();
        MainForm main;
        DateTime lastTickUtc = DateTime.UtcNow;

        public TrayApp()
        {
            Config.Load();

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Information;
            tray.Text = "ScreenWatch";
            tray.Visible = true;
            tray.BalloonTipClicked += delegate { ShowMain(); };
            tray.DoubleClick += delegate { ShowMain(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("選取想要監視的區域…", null, delegate { AddWatch(); });
            menu.Items.Add("開啟主視窗", null, delegate { ShowMain(); });
            menu.Items.Add("設定手機通知…", null, delegate { EditDiscord(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("把移到背景的視窗放回來", null, delegate { UnparkAll(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("結束", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;

            timer = new Timer();
            timer.Interval = 100;       // UI 只負責顯示與發通知，不做抓取
            timer.Tick += OnTick;
            timer.Start();

            Native.BeginHighResolutionTimer();
            sampler = new System.Threading.Thread(SamplerLoop);
            sampler.IsBackground = true;
            sampler.Name = "ScreenWatch sampler";
            sampler.Start();

            moveProc = OnTargetMoved;

            // 攔下最小化：不叫使用者別按，而是改成停到螢幕外繼續監視。
            minimizeProc = OnMinimizeStart;
            minimizeHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZESTART,
                IntPtr.Zero, minimizeProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

            ShowMain();     // 開起來就看得到主視窗和那顆按鈕
        }

        // ------------------------------------------------------------------
        // 新增 / 移除監視
        // ------------------------------------------------------------------

        void AddWatch()
        {
            Rectangle region;
            IntPtr hwnd;
            bool whole;
            bool wasVisible = main != null && main.Visible;
            if (wasVisible) main.Hide();     // 別把自己框進去
            try
            {
                using (var sel = new RegionSelector())
                {
                    if (sel.ShowDialog() != DialogResult.OK) return;
                    region = sel.Selection;
                    hwnd = sel.TargetWindow;
                    whole = sel.WholeWindow;
                }
            }
            finally
            {
                ShowMain();
            }

            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("找不到這塊區域底下的視窗。\r\n"
                    + "請對著某個程式的視窗框選，桌面本身沒辦法監視。", "ScreenWatch");
                return;
            }

            if (whole)
            {
                RECT r;
                Native.GetWindowRect(hwnd, out r);
                region = new Rectangle(r.Left, r.Top, r.Width, r.Height);
            }
            CreateWatch(hwnd, region);
        }

        /// <summary>用視窗標題直接建立監視（給 --watch 啟動參數用）。</summary>
        public bool TryAddWatchByTitle(string titlePart)
        {
            IntPtr hwnd = SelfTest.FindWindow(titlePart);
            if (hwnd == IntPtr.Zero) return false;
            RECT r;
            Native.GetWindowRect(hwnd, out r);
            CreateWatch(hwnd, new Rectangle(r.Left, r.Top, r.Width, r.Height), false);
            return true;
        }

        void CreateWatch(IntPtr hwnd, Rectangle region)
        {
            CreateWatch(hwnd, region, true);
        }

        /// <summary>interactive=false 時跳過設定對話框，直接用預設值（給 --watch 自動化用）。</summary>
        void CreateWatch(IntPtr hwnd, Rectangle region, bool interactive)
        {
            var w = new Watch(hwnd, region);

            // 設定當下先確認這個目標抓不抓得到 —— 抓不到就當場說，
            // 不要讓使用者以為在監視、實際上一直是瞎的。
            string why;
            bool okCapture = Verify(w, out why);

            if (interactive)
            {
                using (var dlg = new WatchSettingsForm(w))
                {
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        w.Dispose();
                        return;
                    }
                    dlg.ApplyTo(w);
                }
            }
            else
            {
                w.Trig.Kind = TriggerKind.Full;
                w.IntervalSeconds = 0.05;
                w.Trig.ChangeThreshold = Math.Max(1.0, w.NoiseFloor * 3);
            }

            if (interactive && !okCapture)
            {
                MessageBox.Show("目前看不到這個視窗的內容：" + why + "\r\n\r\n"
                    + "有些程式（例如全螢幕遊戲、有版權保護的影片）不允許其他程式讀取畫面。\r\n"
                    + "監視還是會建立，但清單上會顯示「看不到畫面」，也不會通知你。",
                    "ScreenWatch");
            }

            w.Triggered += OnWatchTriggered;
            w.ProblemFound += OnWatchProblem;
            lock (gate) watches.Add(w);

            var badge = new Badge(w);
            badge.CloseClicked += delegate(Badge b) { RemoveWatch(b.Target); };
            badges.Add(badge);
            badge.SyncVisibility();

            // 只監聽這個目標所屬行程的位置變化。掛全域的話，滑鼠游標的
            // 移動事件會把訊息佇列灌爆（游標也是一種 accessible object）。
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(hwnd, out pid);
            IntPtr mh = Native.SetWinEventHook(
                Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, moveProc, pid, tid, Native.WINEVENT_OUTOFCONTEXT);
            if (mh != IntPtr.Zero) moveHooks.Add(mh);

            tray.ShowBalloonTip(3000, "已開始監視",
                string.Format("{0}　{1}", w.Title, w.Trig.Describe()), ToolTipIcon.Info);
            ShowMain();
        }

        void RemoveWatch(Watch w)
        {
            if (w == null) return;
            for (int i = badges.Count - 1; i >= 0; i--)
            {
                if (badges[i].Target != w) continue;
                badges[i].Hide();
                badges[i].Dispose();
                badges.RemoveAt(i);
            }
            if (w.IsParked) w.Unpark();
            lock (gate) watches.Remove(w);
            // 取樣執行緒可能正好抓到一半，等它跑完這一輪再釋放 GDI 資源
            System.Threading.Thread.Sleep(40);
            w.Dispose();
            RefreshMain();
        }

        void RefreshMain()
        {
            if (main == null || main.IsDisposed) return;
            List<Watch> copy;
            lock (gate) copy = new List<Watch>(watches);
            main.RefreshList(copy);
        }

        /// <summary>連抓幾張，確認內容有意義，並量出雜訊底線。</summary>
        static bool Verify(Watch w, out string reason)
        {
            reason = "";
            var deltas = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                w.Tick(0, true);
                if (w.Status == WatchStatus.Blind) reason = w.StatusDetail;
                if (i > 0) deltas.Add(w.LastDelta);
                System.Threading.Thread.Sleep(120);
            }

            // 要抓「安靜時的雜訊底線」，不是這段時間的最大變化量。
            // 用最大值會害死持續在變化的目標 —— 門檻被推到真實變化之上，
            // 結果一次都不通知。取低百分位才是雜訊。
            deltas.Sort();
            w.NoiseFloor = deltas.Count > 0 ? deltas[deltas.Count / 5] : 0;
            return reason.Length == 0;
        }

        // ------------------------------------------------------------------
        // 主迴圈
        // ------------------------------------------------------------------

        /// <summary>背景取樣迴圈。每個 Watch 自己判斷時間到了沒。</summary>
        void SamplerLoop()
        {
            while (running)
            {
                Watch[] snapshot;
                lock (gate) snapshot = watches.ToArray();

                for (int i = 0; i < snapshot.Length; i++)
                {
                    try { snapshot[i].Tick(0, false); }
                    catch (Exception) { }
                }

                // 睡到「下一個項目該取樣」為止。固定睡 15ms 的話，
                // 設定每 50ms 一次會變成每 60ms（15 的倍數），頻率直接少四分之一。
                double wait = 15;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    double d = snapshot[i].MsUntilDue;
                    if (d < wait) wait = d;
                }
                System.Threading.Thread.Sleep((int)Math.Max(1, Math.Min(15, wait)));
            }
        }

        /// <summary>取樣執行緒呼叫，只把結果排進佇列；真正的通知留給 UI 執行緒。</summary>
        void Enqueue(Watch w, string reason, Bitmap shot, bool problem)
        {
            lock (gate)
            {
                if (pending.Count > 64)     // 防止 UI 落後時無限堆積
                {
                    Pending drop = pending.Dequeue();
                    if (drop.Shot != null) drop.Shot.Dispose();
                }
                pending.Enqueue(new Pending
                {
                    Watch = w, Reason = reason, Shot = shot, IsProblem = problem
                });
            }
        }

        void OnTick(object sender, EventArgs e)
        {
            lastTickUtc = DateTime.UtcNow;

            while (true)
            {
                Pending p;
                lock (gate)
                {
                    if (pending.Count == 0) break;
                    p = pending.Dequeue();
                }
                if (p.IsProblem) ShowProblem(p.Watch, p.Reason);
                else ShowTrigger(p.Watch, p.Reason, p.Shot);
            }

            Watch[] snapshot;
            lock (gate) snapshot = watches.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Status != WatchStatus.Dead) continue;
                tray.ShowBalloonTip(3000, "監視結束",
                    snapshot[i].Title + " 的視窗已關閉。", ToolTipIcon.Warning);
                RemoveWatch(snapshot[i]);
            }

            for (int i = 0; i < badges.Count; i++)
            {
                try { badges[i].SyncVisibility(); }
                catch (Exception) { }
            }

            int blind = 0;
            List<Watch> copy;
            lock (gate)
            {
                copy = new List<Watch>(watches);
                for (int i = 0; i < watches.Count; i++)
                    if (watches[i].Status == WatchStatus.Blind) blind++;
            }
            tray.Text = string.Format("ScreenWatch — 監視 {0} 項{1}",
                copy.Count, blind > 0 ? string.Format("，{0} 項看不到", blind) : "");

            if (main != null && !main.IsDisposed && main.Visible) main.RefreshList(copy);
        }

        // 這兩個是取樣執行緒呼叫的，只能排隊，不能直接碰 UI
        void OnWatchTriggered(Watch w, string reason, Bitmap shot)
        {
            Enqueue(w, reason, shot, false);
        }

        void OnWatchProblem(Watch w, string problem)
        {
            Enqueue(w, problem, null, true);
        }

        void ShowTrigger(Watch w, string reason, Bitmap shot)
        {
            if (main != null && !main.IsDisposed) main.AddHistory(w.Title + "　" + reason);

            try { SystemSounds.Asterisk.Play(); }
            catch (Exception) { }
            tray.ShowBalloonTip(6000, w.Title, reason, ToolTipIcon.Info);

            // Discord.Send 會負責 Dispose 它拿到的那張圖
            if (w.DiscordEnabled && Discord.Configured)
                Discord.Send(string.Format("**{0}**\n{1}", w.Title, reason), shot);
            else if (shot != null) shot.Dispose();
        }

        /// <summary>設定有問題、永遠不會通知的情況要主動講，不能只安靜地顯示狀態。</summary>
        void ShowProblem(Watch w, string problem)
        {
            if (main != null && !main.IsDisposed) main.AddHistory("⚠ " + w.Title + "　" + problem);
            tray.ShowBalloonTip(8000, "這項監視不會通知你", w.Title + "：" + problem,
                                ToolTipIcon.Warning);
        }

        /// <summary>不經過任何條件，直接走完整條通知路徑。</summary>
        void TestNotify()
        {
            if (main != null && !main.IsDisposed)
                main.AddHistory("通知檢查：已送出。如果右下角沒有跳出來，"
                    + "請到 Windows 設定裡允許 ScreenWatch 顯示通知");
            try { SystemSounds.Asterisk.Play(); }
            catch (Exception) { }
            tray.ShowBalloonTip(6000, "ScreenWatch",
                "通知正常。你看得到這則，就表示之後有變化時也收得到。", ToolTipIcon.Info);
            if (Discord.Configured)
                Discord.Send("ScreenWatch：通知正常，之後有變化會傳到這裡。", null);
        }

        /// <summary>目標視窗一動，浮動關閉鈕就立刻跟上，不用等下一次定時更新。</summary>
        void OnTargetMoved(IntPtr hook, uint ev, IntPtr hwnd,
                           int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != Native.OBJID_WINDOW) return;
            for (int i = 0; i < badges.Count; i++)
            {
                Badge b = badges[i];
                if (b.Target == null || b.Target.Hwnd != hwnd) continue;
                try { b.SyncVisibility(); }
                catch (Exception) { }
                return;
            }
        }

        void OnMinimizeStart(IntPtr hook, uint ev, IntPtr hwnd,
                             int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0) return;            // 只理視窗本身
            Watch[] snapshot;
            lock (gate) snapshot = watches.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Hwnd != hwnd) continue;
                Watch w = snapshot[i];
                if (w.ParkBehaviour == ParkResult.StopsRendering) return;
                w.Park();
                tray.ShowBalloonTip(5000, "已移到背景繼續監視",
                    "視窗一旦最小化就看不到內容了，所以幫你移到背景，監視不會中斷。"
                    + "要拿回來的話，在系統匣圖示上按右鍵選「把移到背景的視窗放回來」。",
                    ToolTipIcon.Info);
                return;
            }
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------

        void UnparkAll()
        {
            Watch[] snapshot;
            lock (gate) snapshot = watches.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i].IsParked) snapshot[i].Unpark();
        }

        void EditWatch(Watch w)
        {
            if (w == null) return;
            using (var dlg = new WatchSettingsForm(w))
            {
                if (dlg.ShowDialog() == DialogResult.OK) dlg.ApplyTo(w);
            }
            RefreshMain();
        }

        void EditDiscord()
        {
            using (var dlg = new DiscordSettingsForm())
            {
                if (dlg.ShowDialog() == DialogResult.OK) dlg.ApplyAndSave();
            }
            RefreshMain();
        }

        void ShowMain()
        {
            if (main == null || main.IsDisposed)
            {
                main = new MainForm();
                main.AddRequested += delegate { AddWatch(); };
                main.RemoveRequested += delegate(Watch w) { RemoveWatch(w); };
                main.EditRequested += delegate(Watch w) { EditWatch(w); };
                main.DiscordSettingsRequested += delegate { EditDiscord(); };
                main.TestNotifyRequested += delegate { TestNotify(); };
            }
            RefreshMain();
            main.Show();
            if (main.WindowState == FormWindowState.Minimized)
                main.WindowState = FormWindowState.Normal;
            main.Activate();
        }

        void ExitApp()
        {
            running = false;                    // 先讓取樣執行緒停下來再釋放 GDI 資源
            timer.Stop();
            Native.EndHighResolutionTimer();
            if (sampler != null && sampler.IsAlive) sampler.Join(500);
            if (minimizeHook != IntPtr.Zero) Native.UnhookWinEvent(minimizeHook);
            for (int i = 0; i < moveHooks.Count; i++) Native.UnhookWinEvent(moveHooks[i]);
            moveHooks.Clear();
            UnparkAll();
            for (int i = 0; i < badges.Count; i++) badges[i].Dispose();
            badges.Clear();
            lock (gate)
            {
                for (int i = 0; i < watches.Count; i++) watches[i].Dispose();
                watches.Clear();
            }
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }

        /// <summary>解析毫秒參數。解析不了就用預設值，不要讓測試入口靜默炸掉。</summary>
        static int ArgMs(string[] args, int index, int fallback)
        {
            int v;
            if (args.Length > index && int.TryParse(args[index], out v) && v > 0) return v;
            return fallback;
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                SelfTest.Run(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--pick")
            {
                SelfTest.Pick(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--discordtest")
            {
                SelfTest.DiscordTest(args[1], args.Length >= 3 && args[2] == "img");
                return;
            }
            // 只用來目視檢查遮罩畫面（含取消鈕）：秀出來幾秒後自己關掉。
            if (args.Length >= 1 && args[0] == "--overlay")
            {
                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int ms = ArgMs(args, 1, 3000);
                using (var sel = new RegionSelector())
                {
                    var t = new Timer();
                    t.Interval = ms;
                    t.Tick += delegate { t.Stop(); t.Dispose(); sel.Close(); };
                    sel.Shown += delegate { t.Start(); };
                    sel.ShowDialog();
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--uitest")
            {
                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.UiTest(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--triggertest")
            {
                Native.MakeDpiAware();
                SelfTest.TriggerTest(args[1]);
                return;
            }
            // 只用來目視檢查設定畫面的版面：秀出來幾秒後自己關掉。
            if (args.Length >= 2 && args[0] == "--settingsui")
            {
                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                IntPtr h = SelfTest.FindWindow(args[1]);
                if (h == IntPtr.Zero) return;
                RECT rr;
                Native.GetWindowRect(h, out rr);
                using (var w = new Watch(h, new Rectangle(rr.Left, rr.Top, rr.Width, rr.Height)))
                using (var dlg = new WatchSettingsForm(w))
                {
                    var t = new Timer();
                    t.Interval = ArgMs(args, 2, 6000);
                    t.Tick += delegate { t.Stop(); t.Dispose(); dlg.Close(); };
                    dlg.Shown += delegate { t.Start(); };
                    dlg.ShowDialog();
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--trace")
            {
                Native.MakeDpiAware();
                SelfTest.Trace(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--ratetest")
            {
                Native.MakeDpiAware();
                SelfTest.RateTest(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--bench")
            {
                Native.MakeDpiAware();
                SelfTest.Bench(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--bartest")
            {
                Native.MakeDpiAware();
                SelfTest.BarTest(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--misstest")
            {
                Native.MakeDpiAware();
                SelfTest.MissTest(args[1]);
                return;
            }

            Native.MakeDpiAware();          // 一定要在建立任何視窗之前
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var app = new TrayApp();
            if (args.Length >= 2 && args[0] == "--watch")
            {
                string title = args[1];
                // 等訊息迴圈跑起來再建立；目標可能比我們晚啟動，所以要重試。
                int tries = 0;
                var t = new Timer();
                t.Interval = 500;
                t.Tick += delegate
                {
                    tries++;
                    if (app.TryAddWatchByTitle(title))
                    {
                        t.Stop();
                        t.Dispose();
                        return;
                    }
                    if (tries >= 30)
                    {
                        t.Stop();
                        t.Dispose();
                        MessageBox.Show("等了 15 秒還是找不到標題含「" + title
                            + "」的視窗。", "ScreenWatch");
                    }
                };
                t.Start();
            }
            Application.Run(app);
        }
    }
}
