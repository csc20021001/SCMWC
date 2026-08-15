using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCMWC
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
            Text = Str.T("授權", "Licence");
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
            note.Text = Str.T(
                "目前所有功能都免費開放，還沒有需要付費才能用的部分。\r\n"
                + "這個畫面是先把授權機制接好；之後推出付費功能時，"
                + "在這裡輸入購買後收到的授權碼就會生效。",
                "Everything is free at the moment — nothing is behind a paywall yet.\r\n"
                + "This screen exists so the licensing is wired up in advance. When paid "
                + "features arrive, entering the key you received on purchase will unlock them.");
            note.ForeColor = Color.FromArgb(95, 95, 95);
            note.SetBounds(Px(16), Px(46), Px(525), Px(52));
            Controls.Add(note);

            var l = new Label();
            l.Text = Str.T("授權碼", "Licence key");
            l.SetBounds(Px(16), Px(108), Px(120), Px(22));
            Controls.Add(l);

            key.SetBounds(Px(16), Px(132), Px(525), Px(26));
            key.Text = License.Key;
            Controls.Add(key);

            var where = new Label();
            where.Text = Str.T("購買後授權碼會寄到你的信箱，也可以在購買確認頁看到。",
                "Your key is emailed to you after purchase, and shown on the confirmation page.");
            where.ForeColor = Color.FromArgb(95, 95, 95);
            where.SetBounds(Px(16), Px(162), Px(525), Px(22));
            Controls.Add(where);

            activate.Text = Str.T("啟用", "Activate");
            activate.SetBounds(Px(16), Px(196), Px(110), Px(30));
            activate.Click += delegate { DoActivate(); };
            Controls.Add(activate);

            deactivate.Text = Str.T("在這台電腦停用", "Deactivate this PC");
            deactivate.SetBounds(Px(134), Px(196), Px(140), Px(30));
            deactivate.Click += delegate { DoDeactivate(); };
            Controls.Add(deactivate);

            result.SetBounds(Px(16), Px(234), Px(525), Px(44));
            result.ForeColor = Color.FromArgb(95, 95, 95);
            Controls.Add(result);

            var close = new Button();
            close.Text = Str.T("關閉", "Close");
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
                    status.Text = Str.T("已啟用", "Active");
                    status.ForeColor = Color.FromArgb(30, 120, 70);
                    break;
                case LicenseState.GracePeriod:
                    status.Text = Str.T("已啟用（離線寬限期）", "Active (offline grace period)");
                    status.ForeColor = Color.FromArgb(150, 100, 20);
                    break;
                case LicenseState.Expired:
                    status.Text = Str.T("授權已失效", "Licence expired");
                    status.ForeColor = Color.FromArgb(170, 60, 40);
                    break;
                default:
                    status.Text = Str.T("尚未啟用", "Not activated");
                    status.ForeColor = Color.FromArgb(80, 80, 80);
                    break;
            }
            deactivate.Enabled = License.Key.Length > 0;
            if (License.LastError.Length > 0) result.Text = License.LastError;
        }

        void DoActivate()
        {
            activate.Enabled = false;
            result.Text = Str.T("連線中…", "connecting…");
            Application.DoEvents();
            bool ok = License.Activate(key.Text);
            result.Text = ok ? Str.T("啟用成功，感謝支持。", "Activated. Thank you for the support.")
                             : (Str.T("啟用失敗：", "Activation failed: ") + License.LastError);
            activate.Enabled = true;
            SyncStatus();
        }

        void DoDeactivate()
        {
            if (MessageBox.Show(Str.T(
                    "要在這台電腦停用這組授權嗎？\r\n停用後名額會還回去，可以拿去啟用別台電腦。",
                    "Deactivate this licence on this computer?\r\nThe seat is returned and can be used on another machine."),
                    "SCMWC", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            deactivate.Enabled = false;
            result.Text = Str.T("連線中…", "connecting…");
            Application.DoEvents();
            bool ok = License.Deactivate();
            result.Text = ok ? Str.T("已停用。", "Deactivated.") : License.LastError;
            key.Text = "";
            SyncStatus();
        }
    }
}
