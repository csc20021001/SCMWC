using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCMWC
{
    /// <summary>
    /// 貼在被監視區域旁邊的小關閉鈕。點它就停掉那一項監視。
    ///
    /// 這是取代「選取清單某列 → 按移除」的做法：那個流程依賴選取狀態，
    /// 而選取狀態有太多方式會被清掉。直接對著目標點叉叉沒有這個問題。
    /// </summary>
    public sealed class Badge : Form
    {
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        readonly int size;
        bool hot;

        public event Action<Badge> CloseClicked;
        public Watch Target;

        public Badge(Watch target)
        {
            Target = target;
            size = (int)Math.Round(26 * Native.SystemScale());

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(size, size);
            BackColor = Color.FromArgb(40, 40, 46);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            var tip = new ToolTip();
            tip.SetToolTip(this, Str.T("停止監視這一項", "Stop watching this"));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // NOACTIVATE：點它不要把焦點從使用者正在用的程式搶走
                // TOOLWINDOW：不要出現在 Alt-Tab
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var h = CloseClicked;
                if (h != null) h(this);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(hot ? Color.FromArgb(210, 60, 60) : Color.FromArgb(40, 40, 46));
            using (var pen = new Pen(Color.FromArgb(180, 255, 255, 255), 1))
                g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
            int inset = size / 3;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.White, Math.Max(1.6f, size / 13f)))
            {
                g.DrawLine(pen, inset, inset, size - inset, size - inset);
                g.DrawLine(pen, size - inset, inset, inset, size - inset);
            }
            base.OnPaint(e);
        }

        /// <summary>
        /// 跟著被監視的區域移動。優先放在區域右上角的「外側」，
        /// 這樣不會蓋住目標內容，也不會跟視窗自己的關閉鈕重疊造成誤點。
        /// </summary>
        public void Reposition()
        {
            if (Target == null) return;
            // 自己重算，不要用上一次取樣時記下的位置
            Rectangle r = Target.RefreshScreenRegion();
            Rectangle virt = SystemInformation.VirtualScreen;
            int margin = Math.Max(2, size / 6);

            int x = r.Right + margin;
            int y = r.Top;
            if (x + size > virt.Right) x = r.Right - size - margin;   // 放不下就改放內側
            if (y < virt.Top) y = virt.Top;
            if (y + size > virt.Bottom) y = virt.Bottom - size;

            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
        }

        public void SyncVisibility()
        {
            bool alive = Target != null
                         && Target.Status != WatchStatus.Dead
                         && !Target.IsParked
                         && Native.IsWindow(Target.Hwnd)
                         && !Native.IsIconic(Target.Hwnd)
                         && !Native.IsCloaked(Target.Hwnd);

            // 被監視的區域被別的視窗蓋住時就收起來。
            // 少了這個判斷，叉叉會一直浮在使用者正在用的程式上面。
            bool show = alive && Native.IsVisibleAt(Target.Hwnd, Target.RefreshScreenRegion());

            if (show)
            {
                Reposition();
                if (!Visible) Show();
                TopMost = true;
            }
            else if (Visible)
            {
                Hide();
            }
        }
    }
}
