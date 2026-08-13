using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenWatch
{
    /// <summary>輸入授權碼、查看授權狀態。</summary>
    public sealed class LicenseForm : Form
    {
        readonly Label status = new Label();
        readonly TextBox key = new TextBox();
        readonly Button activate = new Button();
        readonly Button deactivate = new Button();
        readonly Label result = new Label();
        readonly double s;

        public LicenseForm()
        {
            s = Native.SystemScale();
            Text = "授權";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(560), Px(330));
            Font = new Font("Segoe UI", 9f * (float)s);

            status.Font = new Font("Segoe UI", 12f * (float)s, FontStyle.Bold);
            status.SetBounds(Px(16), Px(14), Px(520), Px(28));
            Controls.Add(status);

            var note = new Label();
            note.Text = "目前所有功能都免費開放，還沒有需要付費才能用的部分。\r\n"
                      + "這個畫面是先把授權機制接好；之後推出付費功能時，"
                      + "在這裡輸入購買後收到的授權碼就會生效。";
            note.ForeColor = Color.FromArgb(95, 95, 95);
            note.SetBounds(Px(16), Px(46), Px(525), Px(52));
            Controls.Add(note);

            var l = new Label();
            l.Text = "授權碼";
            l.SetBounds(Px(16), Px(108), Px(120), Px(22));
            Controls.Add(l);

            key.SetBounds(Px(16), Px(132), Px(525), Px(26));
            key.Text = License.Key;
            Controls.Add(key);

            var where = new Label();
            where.Text = "購買後授權碼會寄到你的信箱，也可以在購買確認頁看到。";
            where.ForeColor = Color.FromArgb(95, 95, 95);
            where.SetBounds(Px(16), Px(162), Px(525), Px(22));
            Controls.Add(where);

            activate.Text = "啟用";
            activate.SetBounds(Px(16), Px(196), Px(110), Px(30));
            activate.Click += delegate { DoActivate(); };
            Controls.Add(activate);

            deactivate.Text = "在這台電腦停用";
            deactivate.SetBounds(Px(134), Px(196), Px(140), Px(30));
            deactivate.Click += delegate { DoDeactivate(); };
            Controls.Add(deactivate);

            result.SetBounds(Px(16), Px(234), Px(525), Px(44));
            result.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(result);

            var close = new Button();
            close.Text = "關閉";
            close.SetBounds(Px(445), Px(288), Px(96), Px(30));
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            SyncStatus();
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        void SyncStatus()
        {
            switch (License.State)
            {
                case LicenseState.Active:
                    status.Text = "已啟用";
                    status.ForeColor = Color.FromArgb(30, 120, 70);
                    break;
                case LicenseState.GracePeriod:
                    status.Text = "已啟用（離線寬限期）";
                    status.ForeColor = Color.FromArgb(150, 100, 20);
                    break;
                case LicenseState.Expired:
                    status.Text = "授權已失效";
                    status.ForeColor = Color.FromArgb(170, 60, 40);
                    break;
                default:
                    status.Text = "尚未啟用";
                    status.ForeColor = Color.FromArgb(80, 80, 80);
                    break;
            }
            deactivate.Enabled = License.Key.Length > 0;
            if (License.LastError.Length > 0) result.Text = License.LastError;
        }

        void DoActivate()
        {
            activate.Enabled = false;
            result.Text = "連線中…";
            Application.DoEvents();
            bool ok = License.Activate(key.Text);
            result.Text = ok ? "啟用成功，感謝支持。"
                             : ("啟用失敗：" + License.LastError);
            activate.Enabled = true;
            SyncStatus();
        }

        void DoDeactivate()
        {
            if (MessageBox.Show("要在這台電腦停用這組授權嗎？\r\n"
                    + "停用後名額會還回去，可以拿去啟用別台電腦。",
                    "ScreenWatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            deactivate.Enabled = false;
            result.Text = "連線中…";
            Application.DoEvents();
            bool ok = License.Deactivate();
            result.Text = ok ? "已停用。" : License.LastError;
            key.Text = "";
            SyncStatus();
        }
    }
}
