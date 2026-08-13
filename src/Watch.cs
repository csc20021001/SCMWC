using System;
using System.Drawing;

namespace ScreenWatch
{
    public enum WatchStatus
    {
        Live,           // 正常在看
        Blind,          // 抓不到有意義的內容（會誠實記錄時間）
        Parked,         // 使用者最小化過，我們把它停到螢幕外繼續看
        Dead            // 目標視窗不見了
    }

    public enum ParkResult
    {
        Unknown,
        StopsRendering      // 目標自己停止繪製（例如 Chromium 的 occlusion 節流）
    }

    public static class StatusText
    {
        /// <summary>狀態顯示給一般使用者看，不要用 Live / Blind 這種字。</summary>
        public static string Of(WatchStatus s)
        {
            switch (s)
            {
                case WatchStatus.Blind: return "看不到畫面";
                case WatchStatus.Parked: return "已收到背景";
                case WatchStatus.Dead: return "視窗已關閉";
                default: return "監視中";
            }
        }
    }

    /// <summary>一個監視項目。</summary>
    public sealed class Watch : IDisposable
    {
        public IntPtr Hwnd;
        public string Title;
        public Rectangle RegionInWindow;    // 相對視窗左上角的實體像素（設定當下）
        public Size SetupWindowSize;
        public double IntervalSeconds = 1.0;
        public bool DiscordEnabled = true;
        public readonly Trigger Trig = new Trigger();

        /// <summary>建立監視時量到的雜訊底線，用來把「靈敏度」換算成實際門檻。</summary>
        public double NoiseFloor;

        public WatchStatus Status = WatchStatus.Live;
        public string StatusDetail = "";
        public double LastDelta;
        public DateTime LastChangeUtc = DateTime.UtcNow;
        public DateTime LastNotifyUtc = DateTime.MinValue;
        public double NotifyCooldownSeconds = 10;

        // 誠實記錄：看得到多久、瞎了多久。使用者有權知道哪段時間沒在看。
        public double LiveSeconds;
        public double BlindSeconds;
        public int SampleCount;             // 實際取樣次數，用來驗證真實達到的頻率

        public ParkResult ParkBehaviour = ParkResult.Unknown;
        public bool IsParked;
        RECT homeRect;
        DateTime parkedAtUtc;
        DateTime lastSampleUtc = DateTime.MinValue;

        // 取樣間隔要加一點抖動。如果被監視的程式剛好也用相同的週期更新畫面，
        // 兩者會鎖相，導致我們每次都固定錯過同樣的那幾步 ——
        // 實測會讓進度條的最高量測值永遠停在 94%，碰不到門檻。
        readonly Random jitter = new Random(Environment.TickCount);
        double dueFactor = 1.0;

        /// <summary>目前這塊被監視區域在螢幕上的位置，用來擺放關閉鈕。</summary>
        public Rectangle ScreenRegion { get; private set; }

        /// <summary>還有幾毫秒才輪到這一項。取樣迴圈用它決定要睡多久。</summary>
        public double MsUntilDue
        {
            get
            {
                if (lastSampleUtc == DateTime.MinValue) return 0;
                double left = IntervalSeconds * dueFactor * 1000
                    - (DateTime.UtcNow - lastSampleUtc).TotalMilliseconds;
                return left > 0 ? left : 0;
            }
        }

        Capturer cap;
        byte[] prevSig;
        double renderScale = 1.0;
        Point paintedOrigin = Point.Empty;
        bool calibrated;
        int capW, capH;

        bool barWarningSent;

        /// <summary>觸發通知：原因字串 + 當下的畫面（呼叫端負責 Dispose）。</summary>
        public event Action<Watch, string, Bitmap> Triggered;

        /// <summary>設定有問題、永遠不會通知的情況，要主動講出來。</summary>
        public event Action<Watch, string> ProblemFound;

        public Watch(IntPtr hwnd, Rectangle screenRegion)
        {
            Hwnd = hwnd;
            Title = Native.TitleOf(hwnd);
            if (Title.Length == 0) Title = "(無標題的視窗)";
            RECT r;
            Native.GetWindowRect(hwnd, out r);
            homeRect = r;
            SetupWindowSize = new Size(r.Width, r.Height);
            RegionInWindow = new Rectangle(screenRegion.X - r.Left, screenRegion.Y - r.Top,
                                           screenRegion.Width, screenRegion.Height);
            ScreenRegion = screenRegion;
            EnsureCapturer(r);
        }

        public void Dispose()
        {
            if (cap != null) { cap.Dispose(); cap = null; }
        }

        void EnsureCapturer(RECT r)
        {
            if (cap != null && capW == r.Width && capH == r.Height) return;
            if (cap != null) cap.Dispose();
            capW = Math.Max(1, r.Width);
            capH = Math.Max(1, r.Height);
            cap = new Capturer(capW, capH);
            renderScale = 1.0;
            paintedOrigin = Point.Empty;
            calibrated = false;
        }

        /// <summary>
        /// 把「設定時框在螢幕上的區域」換算成「PrintWindow 交回來的影像裡的區域」。
        /// 這兩個不是同一個座標系：DPI-unaware 的目標交回來的影像比視窗小。
        /// </summary>
        Rectangle MapRegion(RECT now)
        {
            double sx = SetupWindowSize.Width > 0 ? (double)now.Width / SetupWindowSize.Width : 1.0;
            double sy = SetupWindowSize.Height > 0 ? (double)now.Height / SetupWindowSize.Height : 1.0;
            return new Rectangle(
                paintedOrigin.X + (int)(RegionInWindow.X * sx * renderScale),
                paintedOrigin.Y + (int)(RegionInWindow.Y * sy * renderScale),
                Math.Max(1, (int)(RegionInWindow.Width * sx * renderScale)),
                Math.Max(1, (int)(RegionInWindow.Height * sy * renderScale)));
        }

        void UpdateScreenRegion(RECT now)
        {
            double sx = SetupWindowSize.Width > 0 ? (double)now.Width / SetupWindowSize.Width : 1.0;
            double sy = SetupWindowSize.Height > 0 ? (double)now.Height / SetupWindowSize.Height : 1.0;
            ScreenRegion = new Rectangle(
                now.Left + (int)(RegionInWindow.X * sx),
                now.Top + (int)(RegionInWindow.Y * sy),
                Math.Max(1, (int)(RegionInWindow.Width * sx)),
                Math.Max(1, (int)(RegionInWindow.Height * sy)));
        }

        void Calibrate(RECT now)
        {
            Rectangle bb = cap.PaintedBounds();
            if (bb.Width > 8 && bb.Height > 8 && now.Width > 0)
            {
                renderScale = (double)bb.Width / now.Width;
                paintedOrigin = new Point(bb.X, bb.Y);
            }
            calibrated = true;
        }

        /// <summary>回傳這次是否真的取樣了（時間還沒到就跳過）。</summary>
        public bool Tick(double elapsedSeconds, bool force)
        {
            if (!Native.IsWindow(Hwnd))
            {
                Status = WatchStatus.Dead;
                StatusDetail = "目標視窗已關閉";
                return false;
            }

            if (!force)
            {
                double since = (DateTime.UtcNow - lastSampleUtc).TotalSeconds;
                if (since < IntervalSeconds * dueFactor) return false;
            }
            dueFactor = 0.75 + jitter.NextDouble() * 0.5;   // 下一次的間隔換一個
            double dt = lastSampleUtc == DateTime.MinValue
                ? elapsedSeconds : (DateTime.UtcNow - lastSampleUtc).TotalSeconds;
            lastSampleUtc = DateTime.UtcNow;
            SampleCount++;

            if (Native.IsIconic(Hwnd))
            {
                // 最小化的視窗完全不繪製。不叫使用者別按最小化 ——
                // 直接還原並停到螢幕外，這是實測仍然抓得到的狀態。
                Park();
            }

            RECT now;
            Native.GetWindowRect(Hwnd, out now);
            EnsureCapturer(now);
            UpdateScreenRegion(now);

            bool ok = cap.Grab(Hwnd);
            if (!calibrated) Calibrate(now);

            Rectangle region = MapRegion(now);
            Sample s = cap.Analyse(region, ok);

            if (!s.IsUsable)
            {
                Status = WatchStatus.Blind;
                StatusDetail = s.Unpainted >= 0.5 ? "目標沒有繪製任何內容" : "抓到的是黑畫面";
                BlindSeconds += dt;
                prevSig = null;          // 不要拿無效畫面去比較
                return true;
            }

            LiveSeconds += dt;
            Status = IsParked ? WatchStatus.Parked : WatchStatus.Live;
            StatusDetail = IsParked ? "已停在螢幕外繼續監視" : "";

            // 選了進度類條件卻找不到進度條 —— 這種情況以前只會安靜地顯示
            // 「學習中」然後永遠不通知。必須講清楚。
            if ((Trig.Kind == TriggerKind.Full || Trig.Kind == TriggerKind.PercentReached)
                && Trig.BarNotFound)
            {
                StatusDetail = "在這塊範圍裡找不到進度條，請重新框住那條進度條";
                if (!barWarningSent)
                {
                    barWarningSent = true;
                    var wh = ProblemFound;
                    if (wh != null) wh(this, StatusDetail);
                }
            }

            if (prevSig != null) LastDelta = Capturer.Delta(prevSig, s.Signature);
            else LastDelta = 0;
            prevSig = s.Signature;

            string reason = Trig.Evaluate(cap, region, LastDelta);
            if (LastDelta > Trig.ChangeThreshold) LastChangeUtc = DateTime.UtcNow;

            // 冷卻時間只能套用在「有變化就通知」上，那種條件會連續成立。
            // 邊緣觸發的條件（跑完了 / 到達 % / 變色）一輪只成立一次，
            // 被冷卻擋掉就等於永久消失，因為 Evaluate 已經把 armed 消耗掉了。
            bool blocked = !Trig.IsEdgeTriggered
                && (DateTime.UtcNow - LastNotifyUtc).TotalSeconds < NotifyCooldownSeconds;

            if (reason != null && !blocked)
            {
                LastNotifyUtc = DateTime.UtcNow;
                var h = Triggered;
                // 只有真的要附到 Discord 才做截圖。主視窗已經不顯示預覽了，
                // 每秒 20 次做一張沒人看的 Bitmap 是白花的。
                bool needImage = DiscordEnabled && Discord.Configured
                                 && Config.DiscordAttachImage;
                if (h != null) h(this, reason, needImage ? cap.ToBitmap(region) : null);
            }

            // 停在螢幕外之後長時間毫無變化 → 目標很可能自己停止繪製了
            // （Chromium 的 occlusion 節流就是這樣）。自動放回螢幕上，不要靜默瞎掉。
            if (IsParked && ParkBehaviour == ParkResult.Unknown
                && (DateTime.UtcNow - parkedAtUtc).TotalSeconds > 45
                && (DateTime.UtcNow - LastChangeUtc).TotalSeconds > 45)
            {
                ParkBehaviour = ParkResult.StopsRendering;
                Unpark();
                StatusDetail = "這個程式隱藏時會停止繪製，已自動放回螢幕上";
            }
            return true;
        }

        /// <summary>把視窗從最小化改成停在螢幕外 —— 使用者體感一樣是收起來了。</summary>
        public void Park()
        {
            if (ParkBehaviour == ParkResult.StopsRendering)
            {
                Unpark();
                return;
            }
            RECT r;
            if (Native.GetWindowRect(Hwnd, out r) && r.Left > Native.OFFSCREEN_XY + 1000)
                homeRect = r;
            Native.ShowWindow(Hwnd, Native.SW_RESTORE);
            Native.SetWindowPos(Hwnd, IntPtr.Zero, Native.OFFSCREEN_XY, Native.OFFSCREEN_XY, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (!IsParked) parkedAtUtc = DateTime.UtcNow;
            IsParked = true;
        }

        public void Unpark()
        {
            Native.ShowWindow(Hwnd, Native.SW_RESTORE);
            Native.SetWindowPos(Hwnd, IntPtr.Zero, homeRect.Left, homeRect.Top, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            IsParked = false;
        }

        public string Describe()
        {
            double total = LiveSeconds + BlindSeconds;
            string cover = total > 0
                ? string.Format("　看得到的時間 {0:0}%", LiveSeconds / total * 100) : "";
            string pct = "";
            if (Trig.Kind == TriggerKind.Full || Trig.Kind == TriggerKind.PercentReached)
                pct = Trig.LastPercent >= 0
                    ? string.Format("　目前進度 {0:0}%", Trig.LastPercent)
                    : "　進度 學習中";
            string name = Title.Length > 26 ? Title.Substring(0, 26) + "…" : Title;

            // 失敗原因要緊跟在狀態後面。放在整行最後的話，它會是第一個被
            // 省略號截掉的東西 —— 使用者只看到「看不到畫面」卻不知道為什麼。
            string why = StatusDetail.Length > 0 ? "：" + StatusDetail : "";

            return string.Format("{0}　{1}{2}　{3}{4}{5}{6}",
                name, StatusText.Of(Status), why, Trig.Describe(), pct, cover,
                DiscordEnabled && Discord.Configured ? "　會推 Discord" : "");
        }
    }
}
