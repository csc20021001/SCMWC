using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>
    /// 框選完之後跳出來，讓使用者決定什麼情況要通知。
    ///
    /// 這裡刻意不放任何裸數字門檻。使用者不需要知道「64x64 灰階網格的平均絕對差」
    /// 是什麼，他只需要說「一點點變化就通知我」或「只有明顯變化才通知我」。
    /// 換算成實際數值是程式的事。
    /// </summary>
    public sealed class WatchSettingsForm : Form
    {
        readonly RadioButton anyChange = new RadioButton();
        readonly RadioButton full = new RadioButton();
        readonly RadioButton percent = new RadioButton();
        readonly RadioButton colorChanged = new RadioButton();
        readonly RadioButton wentQuiet = new RadioButton();
        readonly ComboBox quietFor = new ComboBox();
        readonly Label quietLabel = new Label();
        readonly NumericUpDown percentValue = new NumericUpDown();
        readonly ComboBox sensitivity = new ComboBox();
        readonly ComboBox colorSensitivity = new ComboBox();
        readonly ComboBox rate = new ComboBox();
        readonly CheckBox discord = new CheckBox();
        readonly Label sensitivityLabel = new Label();
        readonly Label colorLabel = new Label();
        readonly Label note = new Label();
        readonly double s;

        // 靈敏度 → 雜訊底線的倍數。倍數越小越靈敏。
        static readonly double[] SensitivityMultiplier = new double[] { 1.5, 3.0, 8.0 };
        static readonly double[] ColorTolerances = new double[] { 25, 45, 90 };
        static readonly double[] QuietOptions = new double[] { 5, 10, 30, 60 };
        // 實測 PrintWindow 一次約 20~24ms（讀像素和分析加起來不到 2ms），
        // 所以硬體上限大約 40~50 Hz。0.05 秒已經接近極限，留一點餘裕。
        static readonly double[] RateSeconds = new double[] { 0.05, 0.2, 1.0, 3.0 };

        public WatchSettingsForm(Watch w)
        {
            s = Native.SystemScale();
            Text = Str.T("什麼時候通知我 — ", "When to alert me — ") + w.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(560), Px(590));
            Font = new Font("Segoe UI", 9f * (float)s);

            int y = Px(14);
            AddHeader(Str.T("什麼情況要通知我？", "What should trigger an alert?"), ref y);

            anyChange.Text = Str.T("畫面只要有變化就通知我", "Anything on screen changes");
            Place(anyChange, ref y);

            full.Text = Str.T("跑完了才通知我（進度條跑到滿）", "A progress bar reaches the end");
            Place(full, ref y);

            percent.Text = Str.T("跑到指定進度就通知我", "A progress bar reaches a set point");
            Place(percent, ref y);
            percentValue.Minimum = 1;
            percentValue.Maximum = 100;
            percentValue.Value = w.Trig.PercentTarget;
            AddRow(Str.T("　　跑到幾 % 的時候通知", "      Alert me at this percentage"), percentValue, Px(70), ref y);

            colorChanged.Text = Str.T("顏色變了就通知我（例如紅色變成綠色）", "A colour changes (red turning green, say)");
            Place(colorChanged, ref y);

            wentQuiet.Text = Str.T("本來一直在動，停下來就通知我", "It was moving and then goes quiet");
            Place(wentQuiet, ref y);
            quietFor.DropDownStyle = ComboBoxStyle.DropDownList;
            quietFor.Items.AddRange(new object[] {
                Str.T("停 5 秒就算跑完", "5 seconds of quiet"),
                Str.T("停 10 秒就算跑完", "10 seconds of quiet"),
                Str.T("停 30 秒就算跑完", "30 seconds of quiet"),
                Str.T("停 1 分鐘才算跑完", "1 minute of quiet") });
            quietLabel.Text = Str.T("　　要安靜多久才算跑完", "      How long counts as finished");
            AddRow(quietLabel, quietFor, Px(190), ref y);

            y += Px(8);
            AddHeader(Str.T("細節（用不到就不用動）", "Details — leave these alone if unsure"), ref y);

            sensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
            sensitivity.Items.AddRange(new object[] {
                Str.T("一點點變化就通知我", "Even a tiny change counts"),
                Str.T("普通", "Normal"),
                Str.T("只有明顯變化才通知我", "Only obvious changes count") });
            sensitivity.SelectedIndex = 1;
            sensitivityLabel.Text = Str.T("要多小的變化才算變化", "How small a change counts");
            AddRow(sensitivityLabel, sensitivity, Px(210), ref y);

            colorSensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
            colorSensitivity.Items.AddRange(new object[] {
                Str.T("顏色稍微不同就算", "A slight shift counts"),
                Str.T("顏色明顯不同才算", "Clearly different"),
                Str.T("顏色差很多才算", "Completely different") });
            colorSensitivity.SelectedIndex = 1;
            colorLabel.Text = Str.T("顏色要差多少才算變了", "How different the colour must be");
            AddRow(colorLabel, colorSensitivity, Px(210), ref y);

            rate.DropDownStyle = ComboBoxStyle.DropDownList;
            rate.Items.AddRange(new object[] {
                Str.T("盯緊（每秒 20 次，一閃而過也抓得到）", "Watch closely — 20x a second, catches a flash"),
                Str.T("常常看（每秒 5 次）", "Often — 5x a second"),
                Str.T("普通（每秒 1 次）", "Normal — once a second"),
                Str.T("偶爾看（每 3 秒，最省電）", "Occasionally — every 3s, lightest") });
            rate.SelectedIndex = IndexOfClosest(RateSeconds, w.IntervalSeconds);
            AddRow(Str.T("多久看一次畫面", "How often to look"), rate, Px(290), ref y);

            discord.Text = Str.T("同時推播到 Discord（人不在電腦前也收得到）", "Also send to Discord, so it reaches me away from the PC");
            discord.Checked = w.DiscordEnabled;
            Place(discord, ref y);

            y += Px(4);
            note.Text = Str.T(
                "「跑完了」和「跑到指定進度」是看畫面上那條進度條，會自己在你框的範圍裡"
                + "找出來，所以直接框整個視窗也可以。找不到的話會跳出來告訴你。\r\n"
                + "如果那個進度不是一條長條，而是像「上傳中 22%」這樣的純文字、"
                + "或是一直轉的圈圈、一直捲的訊息，請改用「停下來就通知我」——"
                + "它不看形狀，只看那塊畫面有沒有安靜下來。\r\n"
                + "看得越勤，越不會漏掉一閃而過的畫面，但被監視的那個程式會稍微變慢。",
                "The two progress-bar options locate the bar inside whatever you framed, "
                + "so selecting the whole window is fine. If no bar can be found you will be told.\r\n"
                + "If the progress is not a bar — a line of text like \u0022Uploading 22%\u0022, a spinner, "
                + "a scrolling log — use \u0022it goes quiet\u0022 instead. That one ignores shape and "
                + "simply watches for the region to stop moving.\r\n"
                + "Looking more often catches brief flashes, but slows the watched program slightly.");
            note.ForeColor = Color.FromArgb(95, 95, 95);
            note.SetBounds(Px(14), y, Px(530), Px(100));
            Controls.Add(note);
            y += Px(106);

            var ok = new Button();
            ok.Text = Str.T("開始監視", "Start watching");
            ok.SetBounds(Px(330), y, Px(110), Px(32));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = Str.T("取消", "Cancel");
            cancel.SetBounds(Px(448), y, Px(96), Px(32));
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            switch (w.Trig.Kind)
            {
                case TriggerKind.Full: full.Checked = true; break;
                case TriggerKind.ColorChanged: colorChanged.Checked = true; break;
                case TriggerKind.PercentReached: percent.Checked = true; break;
                case TriggerKind.WentQuiet: wentQuiet.Checked = true; break;
                default: anyChange.Checked = true; break;
            }
            colorSensitivity.SelectedIndex = IndexOfClosest(ColorTolerances, w.Trig.ColorTolerance);
            quietFor.SelectedIndex = IndexOfClosest(QuietOptions, w.Trig.QuietSeconds);

            EventHandler sync = delegate { SyncEnabled(); };
            anyChange.CheckedChanged += sync;
            full.CheckedChanged += sync;
            colorChanged.CheckedChanged += sync;
            percent.CheckedChanged += sync;
            wentQuiet.CheckedChanged += sync;
            SyncEnabled();
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        static int IndexOfClosest(double[] options, double value)
        {
            int best = 0;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < options.Length; i++)
            {
                double d = Math.Abs(options[i] - value);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        void AddHeader(string text, ref int y)
        {
            var l = new Label();
            l.Text = text;
            l.Font = new Font("Segoe UI", 10f * (float)s, FontStyle.Bold);
            l.SetBounds(Px(14), y, Px(520), Px(24));
            Controls.Add(l);
            y += Px(28);
        }

        void Place(Control c, ref int y)
        {
            c.SetBounds(Px(20), y, Px(520), Px(24));
            Controls.Add(c);
            y += Px(28);
        }

        void AddRow(string label, Control c, int controlWidth, ref int y)
        {
            var l = new Label();
            l.Text = label;
            AddRow(l, c, controlWidth, ref y);
        }

        void AddRow(Label label, Control c, int controlWidth, ref int y)
        {
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.SetBounds(Px(24), y, Px(230), Px(26));
            Controls.Add(label);
            c.SetBounds(Px(258), y, controlWidth, Px(26));
            Controls.Add(c);
            y += Px(30);
        }

        void SyncEnabled()
        {
            // 「安靜下來」也要靠變化量判斷有沒有在動，所以靈敏度對它一樣有意義
            sensitivity.Enabled = anyChange.Checked || wentQuiet.Checked;
            sensitivityLabel.Enabled = sensitivity.Enabled;
            colorSensitivity.Enabled = colorChanged.Checked;
            colorLabel.Enabled = colorChanged.Checked;
            percentValue.Enabled = percent.Checked;
            quietFor.Enabled = wentQuiet.Checked;
            quietLabel.Enabled = wentQuiet.Checked;

            // 進度類的條件最怕「滿了一瞬間就歸零」，所以預設看快一點。
            if (full.Checked || percent.Checked) rate.SelectedIndex = 0;
        }

        public void ApplyTo(Watch w)
        {
            if (full.Checked) w.Trig.Kind = TriggerKind.Full;
            else if (colorChanged.Checked) w.Trig.Kind = TriggerKind.ColorChanged;
            else if (percent.Checked) w.Trig.Kind = TriggerKind.PercentReached;
            else if (wentQuiet.Checked) w.Trig.Kind = TriggerKind.WentQuiet;
            else w.Trig.Kind = TriggerKind.AnyChange;

            // 靈敏度是相對於建立監視時量到的雜訊底線，不是絕對數字。
            double baseNoise = Math.Max(0.35, w.NoiseFloor);
            w.Trig.ChangeThreshold = Math.Max(
                0.8, baseNoise * SensitivityMultiplier[sensitivity.SelectedIndex]);
            w.Trig.ColorTolerance = ColorTolerances[colorSensitivity.SelectedIndex];
            w.Trig.QuietSeconds = QuietOptions[quietFor.SelectedIndex];
            w.Trig.PercentTarget = (int)percentValue.Value;
            w.IntervalSeconds = RateSeconds[rate.SelectedIndex];
            w.DiscordEnabled = discord.Checked;
        }
    }
}
