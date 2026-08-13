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
        // 實測 PrintWindow 一次約 20~24ms（讀像素和分析加起來不到 2ms），
        // 所以硬體上限大約 40~50 Hz。0.05 秒已經接近極限，留一點餘裕。
        static readonly double[] RateSeconds = new double[] { 0.05, 0.2, 1.0, 3.0 };

        public WatchSettingsForm(Watch w)
        {
            s = Native.SystemScale();
            Text = "什麼時候通知我 — " + w.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(560), Px(516));
            Font = new Font("Segoe UI", 9f * (float)s);

            int y = Px(14);
            AddHeader("什麼情況要通知我？", ref y);

            anyChange.Text = "畫面只要有變化就通知我";
            Place(anyChange, ref y);

            full.Text = "跑完了才通知我（進度條跑到滿）";
            Place(full, ref y);

            percent.Text = "跑到指定進度就通知我";
            Place(percent, ref y);
            percentValue.Minimum = 1;
            percentValue.Maximum = 100;
            percentValue.Value = w.Trig.PercentTarget;
            AddRow("　　跑到幾 % 的時候通知", percentValue, Px(70), ref y);

            colorChanged.Text = "顏色變了就通知我（例如紅色變成綠色）";
            Place(colorChanged, ref y);

            y += Px(8);
            AddHeader("細節（用不到就不用動）", ref y);

            sensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
            sensitivity.Items.AddRange(new object[] {
                "一點點變化就通知我", "普通", "只有明顯變化才通知我" });
            sensitivity.SelectedIndex = 1;
            sensitivityLabel.Text = "要多小的變化才算變化";
            AddRow(sensitivityLabel, sensitivity, Px(210), ref y);

            colorSensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
            colorSensitivity.Items.AddRange(new object[] {
                "顏色稍微不同就算", "顏色明顯不同才算", "顏色差很多才算" });
            colorSensitivity.SelectedIndex = 1;
            colorLabel.Text = "顏色要差多少才算變了";
            AddRow(colorLabel, colorSensitivity, Px(210), ref y);

            rate.DropDownStyle = ComboBoxStyle.DropDownList;
            rate.Items.AddRange(new object[] {
                "盯緊（每秒 20 次，一閃而過也抓得到）",
                "常常看（每秒 5 次）",
                "普通（每秒 1 次）",
                "偶爾看（每 3 秒，最省電）" });
            rate.SelectedIndex = IndexOfClosest(RateSeconds, w.IntervalSeconds);
            AddRow("多久看一次畫面", rate, Px(290), ref y);

            discord.Text = "同時推播到 Discord（人不在電腦前也收得到）";
            discord.Checked = w.DiscordEnabled;
            Place(discord, ref y);

            y += Px(4);
            note.Text = "「跑完了」和「跑到指定進度」會自己在你框的範圍裡找出那條進度條，"
                      + "所以直接框整個視窗也可以。找不到的話會跳出來告訴你，不會安靜地不通知。\r\n"
                      + "就算進度條滿了立刻歸零重跑，程式也會從「進度突然倒退」判斷出剛剛跑完了。\r\n"
                      + "「盯緊」已經接近硬體極限（抓一張畫面約 20 毫秒），"
                      + "但會讓被監視的那個程式稍微變慢。";
            note.ForeColor = Color.FromArgb(95, 95, 95);
            note.SetBounds(Px(14), y, Px(530), Px(100));
            Controls.Add(note);
            y += Px(106);

            var ok = new Button();
            ok.Text = "開始監視";
            ok.SetBounds(Px(330), y, Px(110), Px(32));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
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
                default: anyChange.Checked = true; break;
            }
            colorSensitivity.SelectedIndex = IndexOfClosest(ColorTolerances, w.Trig.ColorTolerance);

            EventHandler sync = delegate { SyncEnabled(); };
            anyChange.CheckedChanged += sync;
            full.CheckedChanged += sync;
            colorChanged.CheckedChanged += sync;
            percent.CheckedChanged += sync;
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
            sensitivity.Enabled = anyChange.Checked;
            sensitivityLabel.Enabled = anyChange.Checked;
            colorSensitivity.Enabled = colorChanged.Checked;
            colorLabel.Enabled = colorChanged.Checked;
            percentValue.Enabled = percent.Checked;

            // 進度類的條件最怕「滿了一瞬間就歸零」，所以預設看快一點。
            if (full.Checked || percent.Checked) rate.SelectedIndex = 0;
        }

        public void ApplyTo(Watch w)
        {
            if (full.Checked) w.Trig.Kind = TriggerKind.Full;
            else if (colorChanged.Checked) w.Trig.Kind = TriggerKind.ColorChanged;
            else if (percent.Checked) w.Trig.Kind = TriggerKind.PercentReached;
            else w.Trig.Kind = TriggerKind.AnyChange;

            // 靈敏度是相對於建立監視時量到的雜訊底線，不是絕對數字。
            double baseNoise = Math.Max(0.35, w.NoiseFloor);
            w.Trig.ChangeThreshold = Math.Max(
                0.8, baseNoise * SensitivityMultiplier[sensitivity.SelectedIndex]);
            w.Trig.ColorTolerance = ColorTolerances[colorSensitivity.SelectedIndex];
            w.Trig.PercentTarget = (int)percentValue.Value;
            w.IntervalSeconds = RateSeconds[rate.SelectedIndex];
            w.DiscordEnabled = discord.Checked;
        }
    }
}
