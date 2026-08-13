using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>Discord webhook 設定。</summary>
    public sealed class DiscordSettingsForm : Form
    {
        readonly TextBox url = new TextBox();
        readonly CheckBox attach = new CheckBox();
        readonly Label result = new Label();
        readonly double s;

        public DiscordSettingsForm()
        {
            s = Native.SystemScale();
            Text = "Discord 推播設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(560), Px(300));
            Font = new Font("Segoe UI", 9f * (float)s);

            var how = new Label();
            how.Text = "在 Discord 的頻道設定 → 整合 → Webhook 建立一個 webhook，"
                     + "複製它的網址貼在下面。之後通知就會同時推到那個頻道，"
                     + "手機上就收得到。";
            how.SetBounds(Px(14), Px(12), Px(530), Px(56));
            how.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(how);

            var l = new Label();
            l.Text = "Webhook 網址";
            l.SetBounds(Px(14), Px(76), Px(120), Px(24));
            Controls.Add(l);

            url.SetBounds(Px(14), Px(102), Px(530), Px(26));
            url.Text = Config.DiscordWebhook;
            Controls.Add(url);

            attach.Text = "通知時附上那塊畫面的截圖";
            attach.Checked = Config.DiscordAttachImage;
            attach.SetBounds(Px(14), Px(138), Px(400), Px(24));
            Controls.Add(attach);

            var warn = new Label();
            warn.Text = "附上截圖等於把那塊螢幕內容上傳到 Discord，預設關閉。"
                      + "如果監視的是公司內部系統，開啟前請自行確認合不合規。";
            warn.SetBounds(Px(32), Px(162), Px(510), Px(40));
            warn.ForeColor = Color.FromArgb(150, 90, 20);
            Controls.Add(warn);

            result.SetBounds(Px(14), Px(210), Px(530), Px(24));
            result.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(result);

            var test = new Button();
            test.Text = "送測試通知";
            test.SetBounds(Px(14), Px(240), Px(120), Px(30));
            test.Click += delegate { SendTest(); };
            Controls.Add(test);

            var ok = new Button();
            ok.Text = "儲存";
            ok.SetBounds(Px(340), Px(240), Px(100), Px(30));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(Px(448), Px(240), Px(96), Px(30));
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        void SendTest()
        {
            // 測試要用畫面上現在填的網址，不是已儲存的那個
            string saved = Config.DiscordWebhook;
            bool savedAttach = Config.DiscordAttachImage;
            Config.DiscordWebhook = url.Text.Trim();
            Config.DiscordAttachImage = false;
            if (!Discord.Configured)
            {
                result.Text = "網址看起來不對，要以 http:// 或 https:// 開頭。";
                Config.DiscordWebhook = saved;
                Config.DiscordAttachImage = savedAttach;
                return;
            }
            Discord.LastResult = "送出中…";
            Discord.Send("ScreenWatch 測試通知：設定成功，之後有變化會推到這裡。", null);
            result.Text = "送出中…";

            var t = new Timer();
            t.Interval = 700;
            int tries = 0;
            t.Tick += delegate
            {
                tries++;
                result.Text = "結果：" + Discord.LastResult;
                if (Discord.LastResult != "送出中…" || tries > 20)
                {
                    t.Stop();
                    t.Dispose();
                    Config.DiscordAttachImage = savedAttach;
                }
            };
            t.Start();
        }

        public void ApplyAndSave()
        {
            Config.DiscordWebhook = url.Text.Trim();
            Config.DiscordAttachImage = attach.Checked;
            Config.Save();
        }
    }
}
