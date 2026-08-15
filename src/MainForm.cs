using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SCMWC
{
    /// <summary>
    /// 主視窗（深色主控台）。整個畫面自繪 —— WinForms 沒有色票標籤、圓角、
    /// 列內進度條這些東西，全部用 GDI+ 畫。
    ///
    /// 移除監視刻意不依賴「選取狀態」：每一列右邊自己有一個 ✕，
    /// 被監視的區域旁邊也浮著一個。
    /// </summary>
    public sealed class MainForm : Form
    {
        sealed class LogEntry
        {
            public DateTime Time;
            public string Text;
        }

        readonly Panel header = new Panel();
        readonly AccentButton select = new AccentButton();
        readonly Label hint = new Label();
        readonly ListBox list = new ListBox();
        readonly Label logLabel = new Label();
        readonly ListBox log = new ListBox();
        readonly Panel footer = new Panel();
        readonly LinkText testLink = new LinkText();
        readonly LinkText phoneLink = new LinkText();

        readonly Font fTitle, fMeta, fRowTitle, fPill, fRowDetail, fHint, fLog, fLogTime, fSection;
        readonly double s;
        readonly int closeZone;
        int watchCount;
        string phoneStatus = "";

        public event Action AddRequested;
        public event Action<Watch> RemoveRequested;
        public event Action<Watch> EditRequested;
        public event Action DiscordSettingsRequested;
        public event Action TestNotifyRequested;

        public MainForm()
        {
            s = Native.SystemScale();
            closeZone = Px(46);

            fTitle = new Font("Segoe UI", 13.5f * (float)s, FontStyle.Bold);
            fMeta = new Font("Consolas", 9f * (float)s);
            fSection = new Font("Segoe UI", 9f * (float)s, FontStyle.Bold);
            fRowTitle = new Font("Segoe UI", 11.5f * (float)s, FontStyle.Bold);
            fPill = new Font("Consolas", 8.5f * (float)s);
            fRowDetail = new Font("Consolas", 9f * (float)s);
            fHint = new Font("Segoe UI", 9.5f * (float)s);
            fLog = new Font("Segoe UI", 10f * (float)s);
            fLogTime = new Font("Consolas", 9f * (float)s);

            Text = "SCMWC";
            ClientSize = new Size(Px(820), Px(700));
            MinimumSize = new Size(Px(640), Px(540));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f * (float)s);

            // --- 標頭 ---
            header.Dock = DockStyle.Top;
            header.Height = Px(52);
            header.BackColor = Theme.Bg;
            header.Paint += PaintHeader;

            // --- 主要動作 ---
            select.Dock = DockStyle.Top;
            select.Height = Px(60);
            select.Title = Str.T("選取想要監視的區域", "Select a region to watch");
            select.Subtitle = Str.T("Select a region to watch", "");
            select.Margin = new Padding(Px(22), 0, Px(22), 0);
            select.Click += delegate
            {
                var h = AddRequested;
                if (h != null) h();
            };

            hint.Dock = DockStyle.Top;
            hint.Height = Px(58);
            hint.BackColor = Theme.Bg;
            hint.ForeColor = Theme.TextDim;
            hint.Font = fHint;
            hint.Padding = new Padding(Px(24), Px(10), Px(24), 0);
            hint.Text = Str.T(
                "拖曳滑鼠 = 框選自訂區域，點一下 = 選取整個視窗。可以同時監視多個區域。\r\n"
                + "雙擊清單某一列可以改它的通知條件。",
                "Drag to frame a custom region, or click once to pick a whole window.\r\n"
                + "You can watch several at once. Double-click a row to change its condition.");

            // --- 監視清單 ---
            list.Dock = DockStyle.Top;
            list.Height = Px(210);
            list.BorderStyle = BorderStyle.None;
            // 底色用視窗底色：清單沒填滿時不會露出一大塊灰。
            // 列與列之間的分隔線改成自己畫。
            list.BackColor = Theme.Bg;
            list.ForeColor = Theme.Text;
            list.IntegralHeight = false;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Px(76);
            list.DrawItem += DrawWatchRow;
            list.MouseDown += OnListMouseDown;
            list.MouseMove += OnListMouseMove;
            list.DoubleClick += OnListDoubleClick;
            list.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) Remove(list.SelectedItem as Watch);
            };

            // --- 通知紀錄 ---
            logLabel.Dock = DockStyle.Top;
            logLabel.Height = Px(30);
            logLabel.BackColor = Theme.Bg;
            logLabel.Paint += PaintLogLabel;

            log.Dock = DockStyle.Fill;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.LogBg;
            log.ForeColor = Theme.LogText;
            log.IntegralHeight = false;
            log.DrawMode = DrawMode.OwnerDrawFixed;
            log.ItemHeight = Px(34);
            log.DrawItem += DrawLogRow;

            // --- 頁尾 ---
            footer.Dock = DockStyle.Bottom;
            footer.Height = Px(46);
            footer.BackColor = Theme.FooterBg;
            footer.Paint += PaintFooter;

            testLink.Text = Str.T("試發一則通知", "Send a test alert");
            testLink.Font = new Font("Segoe UI", 9.5f * (float)s, FontStyle.Bold);
            // 兩個連結的寬度讓文字自己決定：中英文長度差很多，寫死會被切掉。
            testLink.SizeToText();
            testLink.Location = new Point(Px(22), Px(15));
            testLink.Click += delegate
            {
                var h = TestNotifyRequested;
                if (h != null) h();
            };
            footer.Controls.Add(testLink);

            phoneLink.Text = Str.T("設定手機通知…", "Phone alerts…");
            phoneLink.Font = new Font("Segoe UI", 9.5f * (float)s, FontStyle.Bold);
            phoneLink.SizeToText();
            phoneLink.Location = new Point(testLink.Right + Px(20), Px(15));
            phoneLink.Click += delegate
            {
                var h = DiscordSettingsRequested;
                if (h != null) h();
            };
            footer.Controls.Add(phoneLink);

            var pad = new Panel();
            pad.Dock = DockStyle.Top;
            pad.Height = Px(12);
            pad.BackColor = Theme.Bg;

            Controls.Add(log);
            Controls.Add(logLabel);
            Controls.Add(list);
            Controls.Add(hint);
            Controls.Add(select);
            Controls.Add(pad);
            Controls.Add(header);
            Controls.Add(footer);
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        // ------------------------------------------------------------------
        // 標頭 / 頁尾 / 區段標題
        // ------------------------------------------------------------------

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int y = Px(18);
            Theme.FillRounded(g, new Rectangle(Px(22), y + Px(3), Px(11), Px(11)), Px(3), Theme.Accent);

            int x = Px(41);
            TextRenderer.DrawText(g, "SCMWC", fTitle, new Point(x, y - Px(2)),
                                  Theme.Text, TextFormatFlags.NoPadding);
            x += TextRenderer.MeasureText(g, "SCMWC", fTitle).Width + Px(10);

            string count = string.Format(Str.T("監視中 {0} 項", "{0} watching"), watchCount);
            TextRenderer.DrawText(g, count, fMeta, new Point(x, y + Px(2)),
                                  Theme.TextFaint, TextFormatFlags.NoPadding);

            Size ps = TextRenderer.MeasureText(g, phoneStatus, fMeta);
            TextRenderer.DrawText(g, phoneStatus, fMeta,
                new Point(header.ClientSize.Width - Px(22) - ps.Width, y + Px(2)),
                Theme.TextFaint, TextFormatFlags.NoPadding);
        }

        void PaintLogLabel(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TextRenderer.DrawText(g, Str.T("通知紀錄（最新的在最上面）", "Alert log (newest first)"), fSection,
                new Point(Px(22), Px(10)), Theme.TextDim, TextFormatFlags.NoPadding);

            int today = 0;
            for (int i = 0; i < log.Items.Count; i++)
            {
                var le = log.Items[i] as LogEntry;
                if (le != null && le.Time.Date == DateTime.Today) today++;
            }
            string right = today == 0 ? Str.T("今天還沒有", "none today")
                : string.Format(Str.T("今天 {0} 則", "{0} today"), today);
            Size sz = TextRenderer.MeasureText(g, right, fMeta);
            TextRenderer.DrawText(g, right, fMeta,
                new Point(logLabel.ClientSize.Width - Px(22) - sz.Width, Px(11)),
                Theme.TextFaint, TextFormatFlags.NoPadding);
        }

        void PaintFooter(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, 0, 0, footer.ClientSize.Width, 0);

            string right = Discord.LastResult.Length > 0
                ? Str.T("上次傳送 ", "Last alert — ") + Discord.LastResult : "";
            if (right.Length == 0) return;
            Size sz = TextRenderer.MeasureText(g, right, fMeta);
            TextRenderer.DrawText(g, right, fMeta,
                new Point(footer.ClientSize.Width - Px(22) - sz.Width, Px(17)),
                Theme.TextFaint, TextFormatFlags.NoPadding);
        }

        // ------------------------------------------------------------------
        // 監視列
        // ------------------------------------------------------------------

        Rectangle CloseRectFor(Rectangle row)
        {
            int side = Px(26);
            return new Rectangle(row.Right - closeZone + (closeZone - side) / 2,
                                 row.Top + (row.Height - side) / 2 - Px(1), side, side);
        }

        Watch CloseHitTest(Point p)
        {
            int idx = list.IndexFromPoint(p);
            if (idx < 0 || idx >= list.Items.Count) return null;
            Watch w = list.Items[idx] as Watch;
            if (w == null) return null;
            if (!CloseRectFor(list.GetItemRectangle(idx)).Contains(p)) return null;
            return w;
        }

        void DrawWatchRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle full = e.Bounds;
            Rectangle body = new Rectangle(full.X, full.Y, full.Width, full.Height - 1);

            object item = list.Items[e.Index];
            Watch w = item as Watch;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (w == null)
            {
                // 空清單的提示不畫成一列，直接寫在底色上
                using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, full);
                TextRenderer.DrawText(g, Convert.ToString(item), fHint,
                    new Point(full.X + Px(24), full.Y + Px(18)), Theme.TextFaint,
                    TextFormatFlags.NoPadding);
                return;
            }

            using (var b = new SolidBrush(selected ? Theme.MutedBg : Theme.Row))
                g.FillRectangle(b, body);
            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, body.Left, body.Bottom, body.Right, body.Bottom);

            int x = body.X + Px(16);
            int y = body.Y + Px(12);
            int lineH = Px(22);

            // 第一行：標題 + 狀態色票
            string name = w.Title.Length > 28 ? w.Title.Substring(0, 28) + "…" : w.Title;
            TextRenderer.DrawText(g, name, fRowTitle, new Rectangle(x, y, body.Width, lineH),
                Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            int nameW = TextRenderer.MeasureText(g, name, fRowTitle).Width;

            Color pillFore, pillBack;
            switch (w.Status)
            {
                case WatchStatus.Blind: pillFore = Theme.WarnText; pillBack = Theme.WarnBg; break;
                case WatchStatus.Live: pillFore = Theme.OkText; pillBack = Theme.OkBg; break;
                default: pillFore = Theme.MutedText; pillBack = Theme.MutedBg; break;
            }
            Theme.DrawPill(g, new Point(x + nameW + Px(10), y + Px(2)),
                Str.IsChinese ? w.StatusLabel + " " + w.StatusLabelEn : w.StatusLabelEn,
                fPill, pillFore, pillBack, Px(7), Px(4));

            // 第二行：條件 · 進度 · 覆蓋率 · 手機通知
            y += lineH + Px(2);
            var bits = new List<string>();
            bits.Add(w.ConditionLabel);
            if (w.ShowsProgress)
                bits.Add(w.ProgressPercent >= 0
                    ? string.Format(Str.T("目前進度 {0:0}%", "progress {0:0}%"), w.ProgressPercent)
                    : Str.T("進度 學習中", "progress: learning"));
            bits.Add(string.Format(Str.T("看得到的時間 {0:0}%", "visible {0:0}%"), w.CoveragePercent));
            if (w.PushesToPhone) bits.Add(Str.T("會推手機通知", "phone alerts on"));
            string detail = string.Join(" · ", bits.ToArray());

            int contentW = body.Width - closeZone - Px(24);
            TextRenderer.DrawText(g, detail, fRowDetail, new Rectangle(x, y, contentW, lineH),
                Theme.TextDim,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            // 第三行：進度條，或看不到畫面時的原因
            y += lineH;
            if (w.Status == WatchStatus.Blind && w.StatusDetail.Length > 0)
            {
                TextRenderer.DrawText(g, w.StatusDetail, fRowDetail,
                    new Rectangle(x, y, contentW, lineH), Theme.WarnText,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.VerticalCenter);
            }
            else if (w.ShowsProgress && w.ProgressPercent >= 0)
            {
                int barW = contentW - Px(16);
                int barY = y + lineH / 2 - Px(2);
                Theme.FillRounded(g, new Rectangle(x, barY, barW, Px(4)), Px(2),
                                  Theme.FromHex("#2F2C38"));
                int fillW = (int)(barW * Math.Min(100, w.ProgressPercent) / 100.0);
                if (fillW > 0)
                    Theme.FillRounded(g, new Rectangle(x, barY, fillW, Px(4)), Px(2), Theme.Accent);
            }
            else if (w.StatusDetail.Length > 0)
            {
                TextRenderer.DrawText(g, w.StatusDetail, fRowDetail,
                    new Rectangle(x, y, contentW, lineH), Theme.TextFaint,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.VerticalCenter);
            }

            // 關閉鈕
            Rectangle cr = CloseRectFor(full);
            bool hot = cr.Contains(list.PointToClient(Cursor.Position));
            Theme.DrawRounded(g, cr, Px(6), hot ? Theme.CloseHot : Theme.CloseBorder, 1f);
            using (var pen = new Pen(hot ? Theme.CloseHot : Theme.MutedText, 1.4f))
            {
                var old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int in2 = cr.Width / 3;
                g.DrawLine(pen, cr.Left + in2, cr.Top + in2, cr.Right - in2, cr.Bottom - in2);
                g.DrawLine(pen, cr.Right - in2, cr.Top + in2, cr.Left + in2, cr.Bottom - in2);
                g.SmoothingMode = old;
            }
        }

        void DrawLogRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            var le = log.Items[e.Index] as LogEntry;
            using (var b = new SolidBrush(Theme.LogBg)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.LogLine))
                g.DrawLine(p, e.Bounds.Left + Px(14), e.Bounds.Bottom - 1,
                              e.Bounds.Right - Px(14), e.Bounds.Bottom - 1);
            if (le == null) return;

            TextRenderer.DrawText(g, le.Time.ToString("HH:mm:ss"), fLogTime,
                new Rectangle(e.Bounds.X + Px(16), e.Bounds.Y, Px(80), e.Bounds.Height),
                Theme.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

            var area = new Rectangle(e.Bounds.X + Px(96), e.Bounds.Y,
                                     e.Bounds.Width - Px(110), e.Bounds.Height);
            TextRenderer.DrawText(g, le.Text, fLog, area, Theme.LogText,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                | TextFormatFlags.VerticalCenter);
        }

        // ------------------------------------------------------------------
        // 互動
        // ------------------------------------------------------------------

        void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Watch w = CloseHitTest(e.Location);
            if (w != null) Remove(w);
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            bool onClose = CloseHitTest(e.Location) != null;
            list.Cursor = onClose ? Cursors.Hand : Cursors.Default;
            list.Invalidate();          // 讓 ✕ 的 hover 效果重畫
        }

        void OnListDoubleClick(object sender, EventArgs e)
        {
            Watch w = list.SelectedItem as Watch;
            if (w == null) return;
            var h = EditRequested;
            if (h != null) h(w);
        }

        void Remove(Watch w)
        {
            if (w == null) return;
            var h = RemoveRequested;
            if (h != null) h(w);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;      // 關視窗只是收到系統匣，監視要繼續跑
                Hide();
            }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        // 資料更新
        // ------------------------------------------------------------------

        public Watch SelectedWatch { get { return list.SelectedItem as Watch; } }
        public int RowCount { get { return list.Items.Count; } }

        /// <summary>給 --uitest 用：模擬點某一列的叉叉，走的是跟滑鼠相同的命中判定。</summary>
        public bool TestClickClose(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= list.Items.Count) return false;
            Rectangle x = CloseRectFor(list.GetItemRectangle(rowIndex));
            Watch w = CloseHitTest(new Point(x.Left + x.Width / 2, x.Top + x.Height / 2));
            if (w == null) return false;
            Remove(w);
            return true;
        }

        public void RefreshList(List<Watch> watches)
        {
            watchCount = watches.Count;
            phoneStatus = Discord.Configured
                ? Str.T("手機通知 · 已連結 Discord", "Phone alerts · Discord connected")
                : Str.T("手機通知 · 尚未設定", "Phone alerts · not set up");
            header.Invalidate();
            footer.Invalidate();

            if (SameItems(watches))
            {
                list.Invalidate();      // 內容是自己畫的，重畫就會拿到最新狀態
                return;
            }

            Watch keep = list.SelectedItem as Watch;
            list.BeginUpdate();
            list.Items.Clear();
            if (watches.Count == 0)
                list.Items.Add(Str.T("還沒有監視項目 —— 按上面的按鈕開始",
                    "Nothing is being watched yet — use the button above to start"));
            else
                for (int i = 0; i < watches.Count; i++) list.Items.Add(watches[i]);
            list.EndUpdate();

            // 選取一定要在 EndUpdate 之後才設，而且照物件比對而不是索引。
            if (keep != null)
            {
                int idx = list.Items.IndexOf(keep);
                if (idx >= 0) list.SelectedIndex = idx;
            }
        }

        bool SameItems(List<Watch> watches)
        {
            if (watches.Count == 0) return list.Items.Count == 1 && !(list.Items[0] is Watch);
            if (list.Items.Count != watches.Count) return false;
            for (int i = 0; i < watches.Count; i++)
                if (!ReferenceEquals(list.Items[i], watches[i])) return false;
            return true;
        }

        public void AddHistory(string line)
        {
            var le = new LogEntry();
            le.Time = DateTime.Now;
            le.Text = line;
            log.Items.Insert(0, le);
            while (log.Items.Count > 200) log.Items.RemoveAt(log.Items.Count - 1);
            log.TopIndex = 0;
            logLabel.Invalidate();
        }
    }

    /// <summary>主要動作按鈕：橘色底、雙行文字、hover 變亮。</summary>
    public sealed class AccentButton : Control
    {
        bool hot;
        public string Title = "";
        public string Subtitle = "";

        public AccentButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);

            int m = Margin.Left;
            var r = new Rectangle(m, 0, Math.Max(1, ClientSize.Width - m * 2), ClientSize.Height);
            Theme.FillRounded(g, r, (int)(8 * Native.SystemScale()), hot ? Theme.AccentHot : Theme.Accent);

            var fT = new Font("Segoe UI", 14f * (float)Native.SystemScale(), FontStyle.Bold);
            var fS = new Font("Segoe UI", 9.5f * (float)Native.SystemScale());
            try
            {
                Size tS = TextRenderer.MeasureText(g, Title, fT);
                Size sS = TextRenderer.MeasureText(g, Subtitle, fS);
                int gap = (int)(12 * Native.SystemScale());
                int total = tS.Width + gap + sS.Width;
                int x = r.X + (r.Width - total) / 2;
                int y = r.Y + (r.Height - tS.Height) / 2;
                TextRenderer.DrawText(g, Title, fT, new Point(x, y), Theme.AccentInk,
                                      TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Subtitle, fS,
                    new Point(x + tS.Width + gap, y + (tS.Height - sS.Height) / 2 + 1),
                    Color.FromArgb(150, Theme.AccentInk), TextFormatFlags.NoPadding);
            }
            finally { fT.Dispose(); fS.Dispose(); }
        }
    }

    /// <summary>頁尾的文字連結。</summary>
    public sealed class LinkText : Control
    {
        bool hot;

        public LinkText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.FooterBg;
        }

        /// <summary>把控件縮成剛好包住目前文字的大小，量法跟 OnPaint 畫的方式一致。</summary>
        public void SizeToText()
        {
            Size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Theme.FooterBg)) e.Graphics.FillRectangle(b, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(0, 0),
                hot ? Theme.Accent : Theme.TextSoft, TextFormatFlags.NoPadding);
        }
    }
}
