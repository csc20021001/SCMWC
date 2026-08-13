using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>
    /// 主視窗。開啟程式就看得到，最上面是「選取想要監視的區域」。
    ///
    /// 移除監視的方式刻意不依賴「選取狀態」：每一列右邊自己有一個叉叉，
    /// 被監視的區域旁邊也有一個。之前那顆「移除選取的監視」按鈕要先選一列，
    /// 而選取狀態太容易被清掉，結果就是按了沒反應。
    /// </summary>
    public sealed class MainForm : Form
    {
        readonly Button select = new Button();
        readonly ListBox list = new ListBox();
        readonly Label hint = new Label();
        readonly Panel bottom = new Panel();
        readonly Button discordBtn = new Button();
        readonly Button testBtn = new Button();
        readonly Label lastNotify = new Label();
        readonly ListBox history = new ListBox();
        readonly Label historyLabel = new Label();
        readonly double s;
        readonly int closeZone;

        public event Action AddRequested;
        public event Action<Watch> RemoveRequested;
        public event Action<Watch> EditRequested;
        public event Action DiscordSettingsRequested;
        public event Action TestNotifyRequested;

        public MainForm()
        {
            // WinForms 在 .NET Framework 上不會自動跟著 DPI 縮放，手動乘上去，
            // 否則在 125% 以上的螢幕會小到看不清。
            s = Native.SystemScale();
            closeZone = Px(30);

            Text = "ScreenWatch";
            ClientSize = new Size(Px(780), Px(660));
            MinimumSize = new Size(Px(620), Px(520));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f * (float)s);

            select.Dock = DockStyle.Top;
            select.Height = Px(56);
            select.Text = "選取想要監視的區域";
            select.Font = new Font("Segoe UI", 12f * (float)s, FontStyle.Bold);
            select.FlatStyle = FlatStyle.Flat;
            select.BackColor = Color.FromArgb(0, 120, 212);
            select.ForeColor = Color.White;
            select.FlatAppearance.BorderSize = 0;
            select.Cursor = Cursors.Hand;
            select.Click += delegate
            {
                var h = AddRequested;
                if (h != null) h();
            };

            hint.Dock = DockStyle.Top;
            hint.Height = Px(78);
            hint.Padding = new Padding(Px(4), Px(6), Px(4), Px(6));
            hint.Text = "選取時：拖曳滑鼠 = 框選自訂區域；點一下 = 選取整個視窗。可以同時監視多個。\r\n"
                      + "要停掉某一項：點那一列右邊的 ✕，或點被監視區域旁邊浮著的 ✕。\r\n"
                      + "雙擊某一列可以改它的通知條件。「看不到畫面」的時間會從「看得到的時間」扣掉。";
            hint.ForeColor = Color.FromArgb(95, 95, 95);

            // 自己繪製項目：清單直接存 Watch 物件，更新文字只要 Invalidate()，
            // 不必清空重建，選取狀態也不會被每半秒的更新弄掉。
            list.Dock = DockStyle.Top;
            list.Height = Px(170);
            list.Font = new Font("Consolas", 9.5f * (float)s);
            list.IntegralHeight = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Px(22);
            list.DrawItem += OnDrawItem;
            list.MouseDown += OnListMouseDown;
            list.MouseMove += OnListMouseMove;
            list.DoubleClick += OnListDoubleClick;
            list.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) Remove(list.SelectedItem as Watch);
            };

            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Px(42);

            discordBtn.Text = "Discord 推播設定…";
            discordBtn.SetBounds(Px(6), Px(7), Px(150), Px(28));
            discordBtn.Click += delegate
            {
                var h = DiscordSettingsRequested;
                if (h != null) h();
            };
            bottom.Controls.Add(discordBtn);

            // 用來分辨「條件沒成立」和「成立了但 Windows 沒把通知顯示出來」
            testBtn.Text = "測試通知";
            testBtn.SetBounds(Px(162), Px(7), Px(96), Px(28));
            testBtn.Click += delegate
            {
                var h = TestNotifyRequested;
                if (h != null) h();
            };
            bottom.Controls.Add(testBtn);

            lastNotify.SetBounds(Px(266), Px(12), Px(480), Px(22));
            lastNotify.ForeColor = Color.FromArgb(95, 95, 95);
            bottom.Controls.Add(lastNotify);

            historyLabel.Dock = DockStyle.Top;
            historyLabel.Height = Px(22);
            historyLabel.Padding = new Padding(Px(4), Px(3), 0, 0);
            historyLabel.Text = "通知紀錄（這裡有出現就代表條件成立了，沒跳通知就是 Windows 擋掉）：";
            historyLabel.ForeColor = Color.FromArgb(95, 95, 95);

            history.Dock = DockStyle.Fill;
            history.Font = new Font("Consolas", 9f * (float)s);
            history.IntegralHeight = false;
            history.BorderStyle = BorderStyle.FixedSingle;

            // Dock.Fill 要先加入，Dock.Top 是「最後加入的在最上面」。
            Controls.Add(history);
            Controls.Add(bottom);
            Controls.Add(historyLabel);
            Controls.Add(list);
            Controls.Add(hint);
            Controls.Add(select);
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        // 給 --uitest 用的檢查點
        public Watch SelectedWatch { get { return list.SelectedItem as Watch; } }
        public int RowCount { get { return list.Items.Count; } }

        /// <summary>
        /// 給 --uitest 用：模擬點某一列的叉叉。
        /// 走的是跟真實滑鼠完全相同的命中判定，所以座標算錯會被測出來。
        /// </summary>
        public bool TestClickClose(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= list.Items.Count) return false;
            Rectangle x = CloseRectFor(list.GetItemRectangle(rowIndex));
            Watch w = CloseHitTest(new Point(x.Left + x.Width / 2, x.Top + x.Height / 2));
            if (w == null) return false;
            Remove(w);
            return true;
        }

        /// <summary>某一列的叉叉命中區。</summary>
        Rectangle CloseRectFor(Rectangle rowBounds)
        {
            int pad = Px(5);
            int side = rowBounds.Height - pad * 2;
            return new Rectangle(rowBounds.Right - closeZone + (closeZone - side) / 2,
                                 rowBounds.Top + pad, side, side);
        }

        /// <summary>座標落在哪一列的叉叉上；沒有就回傳 null。</summary>
        Watch CloseHitTest(Point p)
        {
            int idx = list.IndexFromPoint(p);
            if (idx < 0 || idx >= list.Items.Count) return null;
            Watch w = list.Items[idx] as Watch;
            if (w == null) return null;
            if (!CloseRectFor(list.GetItemRectangle(idx)).Contains(p)) return null;
            return w;
        }

        void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Watch w = CloseHitTest(e.Location);
            if (w != null) Remove(w);
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            list.Cursor = CloseHitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
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

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? SystemColors.Highlight : list.BackColor;
            Color fore = selected ? SystemColors.HighlightText : list.ForeColor;

            object item = list.Items[e.Index];
            Watch w = item as Watch;
            string text = w != null ? w.Describe() : Convert.ToString(item);
            if (w == null) fore = selected ? SystemColors.HighlightText
                                           : Color.FromArgb(120, 120, 120);
            if (w != null && w.Status == WatchStatus.Blind && !selected)
                fore = Color.FromArgb(190, 60, 40);

            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);

            Rectangle textArea = new Rectangle(e.Bounds.Left + Px(4), e.Bounds.Top + Px(3),
                Math.Max(1, e.Bounds.Width - closeZone - Px(6)), e.Bounds.Height);
            using (var b = new SolidBrush(fore))
            using (var sf = new StringFormat())
            {
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                e.Graphics.DrawString(text, e.Font, b, textArea, sf);
            }

            if (w != null)
            {
                Rectangle x = CloseRectFor(e.Bounds);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(selected ? Color.White : Color.FromArgb(150, 60, 60), 2f))
                {
                    int inset = x.Width / 4;
                    e.Graphics.DrawLine(pen, x.Left + inset, x.Top + inset,
                                             x.Right - inset, x.Bottom - inset);
                    e.Graphics.DrawLine(pen, x.Right - inset, x.Top + inset,
                                             x.Left + inset, x.Bottom - inset);
                }
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }
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

        /// <summary>
        /// 只有在項目集合真的改變時才重建清單；否則單純重畫。
        /// </summary>
        public void RefreshList(List<Watch> watches)
        {
            lastNotify.Text = Discord.Configured
                ? "Discord：已設定" + (Discord.LastResult.Length > 0
                    ? "　最後一次推播 " + Discord.LastResult : "")
                : "Discord：未設定（按左邊的按鈕貼上 webhook 網址）";

            if (SameItems(watches))
            {
                list.Invalidate();          // 文字是自己畫的，重畫就會拿到最新的 Describe()
                return;
            }

            Watch keep = list.SelectedItem as Watch;
            list.BeginUpdate();
            list.Items.Clear();
            if (watches.Count == 0)
                list.Items.Add("（還沒有監視項目 —— 按上面的按鈕開始）");
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

        /// <summary>把一則通知記進紀錄。最新的放最上面。</summary>
        public void AddHistory(string line)
        {
            history.Items.Insert(0, string.Format("{0:HH:mm:ss}  {1}", DateTime.Now, line));
            while (history.Items.Count > 200) history.Items.RemoveAt(history.Items.Count - 1);
            history.TopIndex = 0;
        }
    }
}
