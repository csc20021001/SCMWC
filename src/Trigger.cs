using System;
using System.Drawing;

namespace ScreenWatch
{
    public enum TriggerKind
    {
        AnyChange,      // 畫面只要有變化就通知
        Full,           // 跑完了才通知
        ColorChanged,   // 顏色變了就通知
        PercentReached  // 跑到指定進度就通知
    }

    /// <summary>
    /// 通知條件。
    ///
    /// 除了 AnyChange 之外都是「邊緣觸發」：條件從不成立變成成立的那一刻才通知一次，
    /// 之後要等條件重新不成立才會再武裝。不然進度條停在 100% 會每秒吵一次。
    /// </summary>
    public sealed class Trigger
    {
        public TriggerKind Kind = TriggerKind.AnyChange;
        public double ChangeThreshold = 1.0;    // AnyChange 用
        public int PercentTarget = 50;          // PercentReached 用
        public double ColorTolerance = 45;      // ColorChanged 用（RGB 距離）

        /// <summary>邊緣觸發的條件不該套用冷卻時間 —— 錯過就是永久錯過。</summary>
        public bool IsEdgeTriggered
        {
            get { return Kind != TriggerKind.AnyChange; }
        }

        bool armed = true;

        // 進度條的兩個端點顏色。無法一開始就知道，要在觀察中學。
        Color fillColor = Color.Empty;
        Color emptyColor = Color.Empty;
        public bool Calibrated { get { return fillColor != Color.Empty; } }

        Color baseColor = Color.Empty;           // ColorChanged 的基準色
        double prevPercent = -1;

        // 使用者框的範圍裡，真正是進度條的那一條橫帶（用比例存，視窗縮放也還準）。
        double bandTop = -1, bandHeight = 0;
        int locateAttempts;
        int stuckCount;
        double stuckAt = -999;

        public double LastPercent = -1;          // -1 = 還算不出來

        /// <summary>找不到進度條。UI 要據此明確告訴使用者，不能只顯示「學習中」。</summary>
        public bool BarNotFound { get { return bandTop < 0 && locateAttempts >= 60; } }

        public string Describe()
        {
            switch (Kind)
            {
                case TriggerKind.Full: return "跑完了才通知";
                case TriggerKind.ColorChanged: return "顏色變了就通知";
                case TriggerKind.PercentReached:
                    return string.Format("跑到 {0}% 就通知", PercentTarget);
                default: return "有變化就通知";
            }
        }

        /// <summary>
        /// 從左往右量進度條填到哪裡。
        ///
        /// 假設：水平、由左至右填滿。使用者框的區域應該就是那條進度條。
        /// 先學會「填滿色」與「未填色」兩個端點顏色，再數從左邊起連續有幾欄接近填滿色。
        /// 兩端顏色還沒學到（整條同色）時回傳 -1，代表「還不知道」而不是 0%。
        /// </summary>
        /// <summary>
        /// 在使用者框的範圍裡找出真正是進度條的那一條橫帶。
        ///
        /// 使用者常常是「點一下選整個視窗」，那樣直接量整個範圍會量到一片背景色，
        /// 左右同色就永遠學不會，結果是一輩子不通知。所以要自己找。
        ///
        /// 進度條的特徵：某條橫帶被切成左右兩塊、各自內部顏色一致、兩塊顏色差很多。
        /// </summary>
        // 進度條左右兩端幾乎一定有邊框或圓角，最外側幾欄不能拿來當參考色。
        const int Trim = 3;
        const int Cols = 60;            // 找進度條位置用
        const int MeasureCols = 150;    // 量百分比用，解析度要細一點

        public string BandInfo = "尚未找到";

        bool TryLocateBar(Capturer cap, Rectangle region)
        {
            // 掃描位置要夠密，厚的進度條才會橫跨好幾條帶，
            // 「上下呼應度」這個判斷才有鑑別力。
            const int Bands = 96;
            int bandPx = Math.Max(3, region.Height / Bands);

            var ok = new bool[Bands];
            var splitAt = new int[Bands];       // 分界線在第幾欄
            var contrast = new double[Bands];
            var cleanliness = new double[Bands];

            for (int b = 0; b < Bands; b++)
            {
                int y = region.Y + b * region.Height / Bands;
                Color[] cols = cap.ColumnColors(
                    new Rectangle(region.X, y, region.Width, bandPx), Cols);
                if (cols.Length < Trim * 2 + 6) continue;

                int lo = Trim, hi = cols.Length - 1 - Trim;
                Color a = cols[lo];
                Color z = cols[hi];
                double split = Capturer.ColorDistance(a, z);
                if (split < 40) continue;                  // 左右同色，不是填到一半的進度條

                int fromLeft = 0;
                while (lo + fromLeft <= hi
                       && Capturer.ColorDistance(cols[lo + fromLeft], a) < 30) fromLeft++;
                int fromRight = 0;
                while (hi - fromRight >= lo
                       && Capturer.ColorDistance(cols[hi - fromRight], z) < 30) fromRight++;

                int span = hi - lo + 1;
                if (fromLeft + fromRight < span * 0.85) continue;
                if (fromLeft < 2 || fromRight < 2) continue;   // 純粹的邊界線不算

                ok[b] = true;
                splitAt[b] = fromLeft;
                contrast[b] = split;
                cleanliness[b] = (double)(fromLeft + fromRight) / span;
            }

            // 進度條是有厚度的：上下相鄰的橫帶應該在「同一個位置」也有分界線。
            // 只用對比度挑會挑到按鈕邊框那種高對比細線。
            double bestScore = -1;
            int bestBand = -1;
            int bestSupport = 0;
            for (int b = 0; b < Bands; b++)
            {
                if (!ok[b]) continue;
                int support = 0;
                for (int d = -4; d <= 4; d++)
                {
                    int n = b + d;
                    if (d == 0 || n < 0 || n >= Bands || !ok[n]) continue;
                    if (Math.Abs(splitAt[n] - splitAt[b]) <= 4) support++;
                }
                // 呼應度太低的一律不接受。進度條是有厚度的，上下一定有同位置的分界線；
                // 呼應度 0 幾乎一定是按鈕邊框之類的細線，鎖上去就永遠量錯。
                if (support < 2) continue;
                double score = support * 10000 + cleanliness[b] * 100 + contrast[b] / 100;
                if (score > bestScore) { bestScore = score; bestBand = b; bestSupport = support; }
            }

            locateAttempts++;
            if (bestBand < 0) return false;     // 這次找不到，下次再找，不要將就

            bandTop = (double)(bestBand * region.Height / Bands) / region.Height;
            bandHeight = (double)bandPx / region.Height;
            BandInfo = string.Format("region 內 y={0:0.0}%~{1:0.0}%（上下呼應度 {2}）",
                bandTop * 100, (bandTop + bandHeight) * 100, bestSupport);
            return true;
        }

        Rectangle BarRegion(Rectangle region)
        {
            if (bandTop < 0) return region;
            return new Rectangle(region.X, region.Y + (int)(region.Height * bandTop),
                                 region.Width,
                                 Math.Max(2, (int)(region.Height * bandHeight)));
        }

        public double MeasurePercent(Capturer cap, Rectangle region)
        {
            if (bandTop < 0 && !TryLocateBar(cap, region)) return -1;

            Color[] cols = cap.ColumnColors(BarRegion(region), MeasureCols);
            if (cols.Length < Trim * 2 + 4) return -1;

            // 一樣要跳過最外側的邊框欄，否則量到的最大值永遠到不了 100%
            int lo = Trim, hi = cols.Length - 1 - Trim;
            Color left = cols[lo];
            Color right = cols[hi];
            if (Capturer.ColorDistance(left, right) > 30)
            {
                fillColor = left;               // 左右不同 → 左邊是填滿色，右邊是未填色
                emptyColor = right;
            }
            if (!Calibrated) return -1;         // 還沒見過部分填滿的狀態

            int span = hi - lo + 1;
            int filled = 0;
            for (int i = lo; i <= hi; i++)
            {
                if (Capturer.ColorDistance(cols[i], fillColor)
                    <= Capturer.ColorDistance(cols[i], emptyColor)) filled++;
                else break;                     // 只算從左邊連續的部分
            }
            return (double)filled / span * 100.0;
        }

        /// <summary>
        /// 判斷這次取樣要不要通知。回傳 null 表示不通知，否則是給使用者看的原因。
        /// </summary>
        public string Evaluate(Capturer cap, Rectangle region, double delta)
        {
            switch (Kind)
            {
                case TriggerKind.AnyChange:
                    return delta > ChangeThreshold
                        ? string.Format("畫面變了（變化量 {0:0.0}）", delta) : null;

                case TriggerKind.ColorChanged:
                    return EvaluateColor(cap, region);

                case TriggerKind.Full:
                case TriggerKind.PercentReached:
                    return EvaluateProgress(cap, region, delta);
            }
            return null;
        }

        string EvaluateColor(Capturer cap, Rectangle region)
        {
            Color now = cap.AverageColor(region);
            if (baseColor == Color.Empty)
            {
                baseColor = now;
                return null;
            }
            if (Capturer.ColorDistance(now, baseColor) <= ColorTolerance) return null;

            Color was = baseColor;
            baseColor = now;                    // 以新顏色為基準，才能偵測下一次變色
            return string.Format("顏色變了（RGB {0},{1},{2} → {3},{4},{5}）",
                was.R, was.G, was.B, now.R, now.G, now.B);
        }

        string EvaluateProgress(Capturer cap, Rectangle region, double delta)
        {
            // 畫面明明一直在變，量到的進度卻長時間紋風不動 → 八成鎖到錯的橫帶了。
            // 重新找一次，不然一次挑錯就會錯到底。
            if (bandTop >= 0)
            {
                if (delta > 1.0 && Math.Abs(LastPercent - stuckAt) < 0.01) stuckCount++;
                else { stuckCount = 0; stuckAt = LastPercent; }
                if (stuckCount > 120)
                {
                    bandTop = -1;
                    stuckCount = 0;
                    BandInfo = "重新尋找中";
                }
            }

            // 「跑完了」不要求剛好 100%：真實的進度條常有邊框或圓角，
            // 量到滿的時候往往只有 96~98%，卡在 99 就永遠不會通知。
            double target = Kind == TriggerKind.Full ? 96 : PercentTarget;
            double pct = MeasurePercent(cap, region);
            LastPercent = pct;
            if (pct < 0) return null;           // 還沒校準，不能亂通知

            double prev = prevPercent;
            prevPercent = pct;

            if (pct >= target)
            {
                if (!armed) return null;
                armed = false;
                return Kind == TriggerKind.Full
                    ? string.Format("跑完了（{0:0}%）", pct)
                    : string.Format("到達 {0}%（目前 {1:0}%）", PercentTarget, pct);
            }

            // 兩次檢查之間就跑完又歸零的情況。
            // 進度大幅倒退就是證據 —— 這是使用者實際遇到的問題：
            // 進度條滿了之後立刻從 0 重跑，取樣間隔太長就完全看不到那個 100%。
            if (armed && prev >= 0 && prev > target * 0.5 && pct < prev - 25)
            {
                armed = false;
                return string.Format(
                    "跑完了（在兩次檢查之間完成，進度已從 {0:0}% 歸零重跑）", prev);
            }

            // 掉回目標以下才重新武裝，避免停在 100% 一直吵
            if (pct < target - 3) armed = true;
            return null;
        }
    }
}
