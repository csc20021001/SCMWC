using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>
    /// 覆蓋整個桌面的遮罩，兩種選法：
    ///   拖曳 → 自訂區域
    ///   點一下 → 游標底下那個視窗的整個範圍
    /// </summary>
    public sealed class RegionSelector : Form
    {
        const int DragThreshold = 6;      // 小於這個距離算「點一下」而不是拖曳

        Point start;
        Point current;
        bool dragging;

        IntPtr hoverWindow = IntPtr.Zero;
        Rectangle hoverRect;              // 螢幕絕對座標
        string hoverTitle = "";

        Rectangle closeRect;              // 取消鈕（本視窗的相對座標）
        bool closeHot;                    // 游標在取消鈕上
        bool pressedOnClose;              // 按下的位置就在取消鈕上

        /// <summary>選到的區域，螢幕絕對座標。</summary>
        public Rectangle Selection { get; private set; }

        /// <summary>使用者是不是選了「整個視窗」。</summary>
        public bool WholeWindow { get; private set; }

        /// <summary>選整個視窗時，那個視窗的 handle。</summary>
        public IntPtr TargetWindow { get; private set; }

        public RegionSelector()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.30;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            Bounds = SystemInformation.VirtualScreen;

            // 取消鈕放在主螢幕右上角
            double sc = Native.SystemScale();
            int size = (int)Math.Round(52 * sc);
            int pad = (int)Math.Round(20 * sc);
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            closeRect = new Rectangle(primary.Right - Bounds.Left - size - pad,
                                      primary.Top - Bounds.Top + pad, size, size);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }
            if (closeRect.Contains(e.Location))
            {
                pressedOnClose = true;      // 取消鈕上按下就不要開始拖曳
                Invalidate();
                base.OnMouseDown(e);
                return;
            }
            dragging = true;
            start = e.Location;
            current = e.Location;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = closeRect.Contains(e.Location);
            if (hot != closeHot)
            {
                closeHot = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Cross;
                Invalidate();
            }

            if (dragging)
            {
                current = e.Location;
                Invalidate();
            }
            else if (!hot)
            {
                UpdateHover(e.Location);
            }
            else if (hoverWindow != IntPtr.Zero)
            {
                hoverWindow = IntPtr.Zero;  // 游標在取消鈕上時不要highlight視窗
                hoverRect = Rectangle.Empty;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressedOnClose && e.Button == MouseButtons.Left)
            {
                pressedOnClose = false;
                if (closeRect.Contains(e.Location))
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }
                Invalidate();
                base.OnMouseUp(e);
                return;
            }
            if (!dragging || e.Button != MouseButtons.Left)
            {
                base.OnMouseUp(e);
                return;
            }
            dragging = false;

            int dx = Math.Abs(e.Location.X - start.X);
            int dy = Math.Abs(e.Location.Y - start.Y);

            if (dx < DragThreshold && dy < DragThreshold)
            {
                // 點一下 → 整個視窗
                UpdateHover(e.Location);
                if (hoverWindow == IntPtr.Zero || hoverRect.Width < 8 || hoverRect.Height < 8)
                {
                    DialogResult = DialogResult.Cancel;
                }
                else
                {
                    Selection = hoverRect;
                    TargetWindow = hoverWindow;
                    WholeWindow = true;
                    DialogResult = DialogResult.OK;
                }
            }
            else
            {
                Rectangle local = Normalise(start, e.Location);
                Selection = new Rectangle(local.X + Bounds.Left, local.Y + Bounds.Top,
                                          local.Width, local.Height);
                TargetWindow = TopWindowAt(
                    Selection.X + Selection.Width / 2, Selection.Y + Selection.Height / 2);
                WholeWindow = false;
                DialogResult = DialogResult.OK;
            }
            Close();
            base.OnMouseUp(e);
        }

        void UpdateHover(Point local)
        {
            int sx = local.X + Bounds.Left;
            int sy = local.Y + Bounds.Top;
            IntPtr h = TopWindowAt(sx, sy);
            if (h == hoverWindow) return;
            hoverWindow = h;
            if (h == IntPtr.Zero)
            {
                hoverRect = Rectangle.Empty;
                hoverTitle = "";
            }
            else
            {
                RECT r;
                Native.GetWindowRect(h, out r);
                hoverRect = new Rectangle(r.Left, r.Top, r.Width, r.Height);
                hoverTitle = Native.TitleOf(h);
                if (hoverTitle.Length == 0) hoverTitle = Str.T("(無標題的視窗)", "(untitled window)");
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            using (var f = new Font("Segoe UI", 13))
            using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.DrawString(Str.T("拖曳 = 框選自訂區域　　點一下 = 選取整個視窗　　右上角 ✕ 或 Esc = 取消",
                             "Drag to frame a region    ·    Click once to pick a whole window    ·    ✕ or Esc to cancel"),
                             f, b, 24, 20);
            }

            DrawCloseButton(g);

            if (dragging)
            {
                Rectangle r = Normalise(start, current);
                DrawSelection(g, r, string.Format("{0} x {1}", r.Width, r.Height),
                              Color.FromArgb(255, 90, 200, 255));
                return;
            }

            if (hoverWindow != IntPtr.Zero && hoverRect.Width > 0)
            {
                Rectangle local = new Rectangle(hoverRect.X - Bounds.Left,
                                               hoverRect.Y - Bounds.Top,
                                               hoverRect.Width, hoverRect.Height);
                string label = hoverTitle.Length > 60
                    ? hoverTitle.Substring(0, 60) + "…" : hoverTitle;
                DrawSelection(g, local,
                    string.Format(Str.T("整個視窗：{0}  ({1} x {2})", "Whole window: {0}  ({1} x {2})"),
                                  label, hoverRect.Width, hoverRect.Height),
                    Color.FromArgb(255, 120, 230, 140));
            }
        }

        void DrawCloseButton(Graphics g)
        {
            Color fill = pressedOnClose ? Color.FromArgb(255, 150, 30, 30)
                       : closeHot ? Color.FromArgb(255, 210, 60, 60)
                                  : Color.FromArgb(215, 40, 40, 46);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, closeRect);
            using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 1))
                g.DrawRectangle(pen, closeRect);

            int inset = closeRect.Width / 3;
            using (var pen = new Pen(Color.White, Math.Max(2f, closeRect.Width / 16f)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawLine(pen, closeRect.Left + inset, closeRect.Top + inset,
                                closeRect.Right - inset, closeRect.Bottom - inset);
                g.DrawLine(pen, closeRect.Right - inset, closeRect.Top + inset,
                                closeRect.Left + inset, closeRect.Bottom - inset);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }
        }

        void DrawSelection(Graphics g, Rectangle r, string label, Color accent)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            // 把選取範圍挖空，讓底下真正的內容看得清楚
            using (var clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                g.FillRectangle(clear, r);
            using (var pen = new Pen(accent, 2))
                g.DrawRectangle(pen, r);
            using (var f = new Font("Consolas", 10.5f))
            using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var fg = new SolidBrush(Color.White))
            {
                SizeF sz = g.MeasureString(label, f);
                float ly = r.Y - sz.Height - 4;
                if (ly < 0) ly = r.Y + 4;
                g.FillRectangle(bg, r.X, ly, sz.Width + 8, sz.Height + 2);
                g.DrawString(label, f, fg, r.X + 4, ly);
            }
        }

        static Rectangle Normalise(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                                 Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        /// <summary>
        /// 座標底下最上層的「真正的」視窗。
        /// 不能用 WindowFromPoint —— 那會回傳我們自己的遮罩。改成照 z 序列舉，
        /// 並濾掉隱形（cloaked）、最小化、我們自己、以及桌面本身。
        /// </summary>
        public IntPtr TopWindowAt(int sx, int sy)
        {
            return TopWindowAt(sx, sy, Handle);
        }

        public static IntPtr TopWindowAt(int sx, int sy, IntPtr me)
        {
            // 挑目標時要連我們自己的視窗（主視窗、浮動關閉鈕）一起排除
            return Native.TopWindowAt(sx, sy, me, true);
        }
    }
}
