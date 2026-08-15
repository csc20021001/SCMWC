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
            Text = Str.T("設定手機通知", "Phone alerts");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(560), Px(300));
            Font = new Font("Segoe UI", 9f * (float)s);

            var how = new Label();
            how.Text = Str.T(
                "人不在電腦前也想收到通知的話，可以把通知同時送到 Discord。\r\n"
                + "在 Discord 的「頻道設定 → 整合 → Webhook」建立一個，"
                + "把它的網址複製貼到下面。手機裝了 Discord 就收得到。",
                "To get alerts when you are away from the PC, send them to Discord as well.\r\n"
                + "In Discord go to Channel Settings > Integrations > Webhooks, create one, "
                + "and paste its URL below. Install Discord on your phone and it will buzz.");
            how.SetBounds(Px(14), Px(12), Px(530), Px(56));
            how.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(how);

            var l = new Label();
            l.Text = Str.T("Discord Webhook 網址", "Discord webhook URL");
            l.SetBounds(Px(14), Px(76), Px(120), Px(24));
            Controls.Add(l);

            url.SetBounds(Px(14), Px(102), Px(530), Px(26));
            url.Text = Config.DiscordWebhook;
            Controls.Add(url);

            attach.Text = Str.T("通知時附上那塊畫面的截圖", "Attach a screenshot of the region");
            attach.Checked = Config.DiscordAttachImage;
            attach.SetBounds(Px(14), Px(138), Px(400), Px(24));
            Controls.Add(attach);

            var warn = new Label();
            warn.Text = Str.T(
                "附上截圖等於把那塊螢幕內容上傳到 Discord，預設關閉。"
                + "如果監視的是公司內部系統，開啟前請自行確認合不合規。",
                "Attaching a screenshot uploads that part of your screen to Discord. Off by default. "
                + "If you are watching an internal company system, check your policy before enabling it.");
            warn.SetBounds(Px(32), Px(162), Px(510), Px(40));
            warn.ForeColor = Color.FromArgb(150, 90, 20);
            Controls.Add(warn);

            result.SetBounds(Px(14), Px(210), Px(530), Px(24));
            result.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(result);

            var test = new Button();
            test.Text = Str.T("試傳一則", "Send a test");
            test.SetBounds(Px(14), Px(240), Px(120), Px(30));
            test.Click += delegate { SendTest(); };
            Controls.Add(test);

            var ok = new Button();
            ok.Text = Str.T("儲存", "Save");
            ok.SetBounds(Px(340), Px(240), Px(100), Px(30));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = Str.T("取消", "Cancel");
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
                result.Text = Str.T("網址看起來不對，要以 http:// 或 https:// 開頭。",
                "That URL does not look right — it should start with http:// or https://");
                Config.DiscordWebhook = saved;
                Config.DiscordAttachImage = savedAttach;
                return;
            }
            Discord.LastResult = Str.T("傳送中…", "sending…");
            Discord.Send(Str.T("ScreenWatch：設定成功，之後有變化會傳到這裡。",
                "ScreenWatch: set up correctly. Changes will show up here."), null);
            result.Text = Str.T("傳送中…", "sending…");

            var t = new Timer();
            t.Interval = 700;
            int tries = 0;
            t.Tick += delegate
            {
                tries++;
                result.Text = Str.T("結果：", "Result: ") + Discord.LastResult;
                if (Discord.LastResult != Str.T("傳送中…", "sending…") || tries > 20)
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
