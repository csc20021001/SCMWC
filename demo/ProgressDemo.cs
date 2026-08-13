using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatchDemo
{
    /// <summary>
    /// ScreenWatch 的測試目標：一個會跑進度條的視窗。
    ///
    /// 刻意做出三種不同性質的變化，方便測不同的偵測條件：
    ///   1. 進度條本身      —— 持續小幅變化
    ///   2. 百分比數字      —— 小面積文字變化（最難偵測的那種）
    ///   3. 完成時整塊變色  —— 大面積劇烈變化
    /// </summary>
    public sealed class DemoForm : Form
    {
        readonly ProgressBar bar = new ProgressBar();
        readonly Label percent = new Label();
        readonly Label phase = new Label();
        readonly ComboBox speed = new ComboBox();
        readonly CheckBox noHold = new CheckBox();
        readonly Button restart = new Button();
        readonly Panel banner = new Panel();
        readonly Timer timer = new Timer();

        int value;
        int holdTicks;
        readonly double s;

        public DemoForm()
        {
            s = SystemScale();

            Text = "Progress Demo — ScreenWatch 測試用";
            ClientSize = new Size(Px(520), Px(300));
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Px(80), Px(80));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(250, 250, 252);
            Font = new Font("Segoe UI", 9f * (float)s);

            banner.Dock = DockStyle.Top;
            banner.Height = Px(70);
            banner.BackColor = Color.FromArgb(200, 60, 60);

            phase.Dock = DockStyle.Fill;
            phase.TextAlign = ContentAlignment.MiddleCenter;
            phase.Font = new Font("Segoe UI", 20f * (float)s, FontStyle.Bold);
            phase.ForeColor = Color.White;
            phase.Text = "Processing...";
            banner.Controls.Add(phase);

            percent.Dock = DockStyle.Top;
            percent.Height = Px(80);
            percent.TextAlign = ContentAlignment.MiddleCenter;
            percent.Font = new Font("Consolas", 34f * (float)s);
            percent.Text = "0%";

            bar.Dock = DockStyle.Top;
            bar.Height = Px(34);
            bar.Maximum = 100;
            bar.Style = ProgressBarStyle.Continuous;

            var pad = new Panel();
            pad.Dock = DockStyle.Top;
            pad.Height = Px(14);
            pad.BackColor = Color.Transparent;

            var row = new Panel();
            row.Dock = DockStyle.Top;
            row.Height = Px(44);

            var speedLabel = new Label();
            speedLabel.Text = "速度";
            speedLabel.Dock = DockStyle.Left;
            speedLabel.Width = Px(44);
            speedLabel.TextAlign = ContentAlignment.MiddleLeft;

            speed.Dock = DockStyle.Left;
            speed.Width = Px(120);
            speed.DropDownStyle = ComboBoxStyle.DropDownList;
            speed.Items.AddRange(new object[] { "慢 (30 秒)", "中 (12 秒)", "快 (5 秒)" });
            speed.SelectedIndexChanged += delegate { ApplySpeed(); };

            // 重現「滿了立刻歸零」：這種目標最容易讓監視工具錯過那個 100%
            noHold.Dock = DockStyle.Left;
            noHold.Width = Px(190);
            noHold.Text = "跑完立刻歸零（不停留）";
            noHold.TextAlign = ContentAlignment.MiddleLeft;

            restart.Dock = DockStyle.Right;
            restart.Width = Px(110);
            restart.Text = "重新開始";
            restart.Click += delegate { value = 0; holdTicks = 0; Render(); };

            row.Controls.Add(restart);
            row.Controls.Add(noHold);
            row.Controls.Add(speed);
            row.Controls.Add(speedLabel);

            Controls.Add(row);
            Controls.Add(pad);
            Controls.Add(bar);
            Controls.Add(percent);
            Controls.Add(banner);

            timer.Tick += delegate { Step(); };
            // 選取項目要等控件都加進表單、handle 建立之後再設，
            // 在那之前設會被 handle 建立過程重置。
            speed.SelectedIndex = 1;
            ApplySpeed();
            timer.Start();
            Render();
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        void ApplySpeed()
        {
            int seconds = speed.SelectedIndex == 0 ? 30 : (speed.SelectedIndex == 2 ? 5 : 12);
            timer.Interval = Math.Max(20, seconds * 1000 / 100);
        }

        void Step()
        {
            if (value >= 100)
            {
                if (noHold.Checked)
                {
                    value = 0;          // 滿了立刻歸零，完全不停留
                    holdTicks = 0;
                }
                else
                {
                    // 停在 Completed 一段時間，方便測「文字從 Processing 變成 Completed」
                    holdTicks++;
                    if (holdTicks * timer.Interval >= 5000)
                    {
                        value = 0;
                        holdTicks = 0;
                    }
                }
            }
            else
            {
                value++;
            }
            Render();
        }

        void Render()
        {
            bar.Value = value;
            percent.Text = value + "%";
            bool done = value >= 100;
            phase.Text = done ? "Completed" : "Processing...";
            banner.BackColor = done ? Color.FromArgb(40, 160, 90) : Color.FromArgb(200, 60, 60);
        }

        static double SystemScale()
        {
            try
            {
                uint d = GetDpiForSystem();
                if (d >= 96) return d / 96.0;
            }
            catch (Exception) { }
            return 1.0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        /// <summary>給自動測試用：--fast 選最快速度，--nohold 跑完立刻歸零。</summary>
        public void ApplyArgs(string[] args)
        {
            foreach (string a in args)
            {
                if (a == "--fast") speed.SelectedIndex = 2;
                if (a == "--nohold") noHold.Checked = true;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new DemoForm();
            f.ApplyArgs(args);
            Application.Run(f);
        }
    }
}
