using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SCMWCSetup
{
    /// <summary>
    /// 安裝程式是獨立編譯的組件，拿不到主程式的 Str，所以自己帶一份。
    /// 這裡只看系統語言：安裝當下還沒有設定檔可以讀。
    /// </summary>
    public static class Str
    {
        static readonly bool chinese = Detect();

        public static bool IsChinese { get { return chinese; } }

        static bool Detect()
        {
            try
            {
                return System.Globalization.CultureInfo.CurrentUICulture
                    .TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        public static string T(string zh, string en) { return chinese ? zh : en; }
    }

    /// <summary>
    /// 單檔安裝程式。把 SCMWC.exe / ProgressDemo.exe / 說明文件
    /// 以資源方式內嵌，安裝時解出來，並建立捷徑與解除安裝項目。
    ///
    /// 不使用任何第三方打包工具：整包就是 Windows 內建的 csc.exe 編出來的一個 exe。
    /// </summary>
    public static class Setup
    {
        public const string AppName = "SCMWC";
        public const string Version = "0.1.3";
        const string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SCMWC";

        public static string InstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", AppName));
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--uninstall")
            {
                Uninstall();
                return;
            }
            Application.Run(new SetupForm());
        }

        public static string ReadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static void Extract(string name, string target)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("missing embedded resource: " + name);
                using (var f = File.Create(target)) s.CopyTo(f);
            }
        }

        public static void Install(bool desktopShortcut)
        {
            StopRunning();
            RemoveLegacyInstall();
            Directory.CreateDirectory(InstallDir);

            Extract("SCMWC.exe", Path.Combine(InstallDir, "SCMWC.exe"));
            Extract("EULA.txt", Path.Combine(InstallDir, "terms-zh.txt"));
            Extract("EULA-en.txt", Path.Combine(InstallDir, "terms.txt"));

            // 安裝程式自己留一份當解除安裝用
            string setupCopy = Path.Combine(InstallDir, "Uninstall.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, setupCopy, true);

            // 記錄同意條款的時間點與版本
            File.WriteAllText(Path.Combine(InstallDir, "accepted-eula.txt"),
                string.Format("version {0}\r\naccepted {1:yyyy-MM-dd HH:mm:ss}\r\nuser {2}\r\n",
                    Version, DateTime.Now, Environment.UserName),
                new System.Text.UTF8Encoding(true));

            string exe = Path.Combine(InstallDir, "SCMWC.exe");
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            CreateShortcut(Path.Combine(programs, AppName + ".lnk"), exe,
                           Str.T("框住螢幕任何一塊，變了就通知你",
                                 "Frame any part of your screen and get told when it changes"));
            if (desktopShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktop, AppName + ".lnk"), exe,
                               Str.T("框住螢幕任何一塊，變了就通知你",
                                     "Frame any part of your screen and get told when it changes"));
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + setupCopy + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 200, RegistryValueKind.DWord);
            }
        }

        /// <summary>
        /// 這個產品在 0.1.2 之前叫 ScreenWatch。直接裝新版會留下一份還能執行的舊程式、
        /// 一個舊捷徑和「應用程式與功能」裡的舊項目，等於同一個工具跑兩份、通知兩次。
        /// 所以升級時把舊的那份收乾淨。
        ///
        /// 只在舊資料夾裡確實有 ScreenWatch.exe 時才刪 —— 不對來路不明的資料夾動手。
        /// 設定檔不刪，Config 會把它搬到新位置。
        /// </summary>
        static void RemoveLegacyInstall()
        {
            const string legacyName = "ScreenWatch";

            foreach (Process p in Process.GetProcessesByName(legacyName))
            {
                try { p.Kill(); p.WaitForExit(3000); }
                catch (Exception) { }
            }

            TryDelete(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.Programs), legacyName + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory), legacyName + ".lnk"));

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + legacyName, false);
            }
            catch (Exception) { }

            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", legacyName));
                if (File.Exists(Path.Combine(dir, legacyName + ".exe")))
                    Directory.Delete(dir, true);
            }
            catch (Exception) { }
        }

        static void Uninstall()
        {
            if (MessageBox.Show(Str.T(
                "要移除 SCMWC 嗎？\r\n\r\n設定檔（%LOCALAPPDATA%\\SCMWC\\config.txt）會一併刪除，"
                + "裡面包含你填的 Discord webhook 網址。",
                "Remove SCMWC?\r\n\r\nThe settings file (%LOCALAPPDATA%\\SCMWC\\config.txt) "
                + "is deleted too — it contains the Discord webhook URL you entered."),
                Str.T("移除 SCMWC", "Remove SCMWC"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;

            StopRunning();

            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            TryDelete(Path.Combine(programs, AppName + ".lnk"));
            TryDelete(Path.Combine(desktop, AppName + ".lnk"));

            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), AppName);
                if (Directory.Exists(cfg)) Directory.Delete(cfg, true);
            }
            catch (Exception) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); }
            catch (Exception) { }

            // 自己刪不掉自己，交給一個延遲的批次檔
            string bat = Path.Combine(Path.GetTempPath(), "scmwc-cleanup.bat");
            File.WriteAllText(bat,
                "@echo off\r\n"
                + "ping 127.0.0.1 -n 3 >nul\r\n"
                + "rmdir /s /q \"" + InstallDir + "\"\r\n"
                + "del \"%~f0\"\r\n");
            var psi = new ProcessStartInfo(bat);
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            Process.Start(psi);

            MessageBox.Show(Str.T("已移除。", "Removed."), AppName);
        }

        static void StopRunning()
        {
            foreach (string n in new string[] { "SCMWC" })
            {
                foreach (Process p in Process.GetProcessesByName(n))
                {
                    try { p.Kill(); p.WaitForExit(3000); }
                    catch (Exception) { }
                }
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        /// <summary>用 WScript.Shell 建捷徑（晚期繫結，不需要額外參考組件）。</summary>
        static void CreateShortcut(string lnkPath, string target, string description)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                                           null, shell, new object[] { lnkPath });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc,
                                new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                                new object[] { Path.GetDirectoryName(target) });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                                new object[] { description });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, new object[0]);
            }
            catch (Exception) { }
        }
    }

    public sealed class SetupForm : Form
    {
        readonly TextBox eula = new TextBox();
        readonly CheckBox agree = new CheckBox();
        readonly CheckBox desktop = new CheckBox();
        readonly Button install = new Button();
        readonly Label pathLabel = new Label();
        readonly double s;

        public SetupForm()
        {
            s = Scale();
            Text = Str.T("安裝 SCMWC ", "Install SCMWC ") + Setup.Version;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(640), Px(600));
            Font = new Font("Segoe UI", 9f * (float)s);

            var title = new Label();
            title.Text = Str.T("使用條款與免責聲明", "Licence and disclaimer");
            title.Font = new Font("Segoe UI", 12f * (float)s, FontStyle.Bold);
            title.SetBounds(Px(16), Px(12), Px(600), Px(28));
            Controls.Add(title);

            var sub = new Label();
            sub.Text = Str.T(
                "請完整閱讀。這份聲明說明了本軟體會做什麼、不保證什麼、以及哪些用途是禁止的。",
                "Please read it in full. It covers what this software does, what it does not "
                + "guarantee, and which uses are forbidden.");
            sub.ForeColor = Color.FromArgb(95, 95, 95);
            sub.SetBounds(Px(16), Px(42), Px(610), Px(36));
            Controls.Add(sub);

            eula.Multiline = true;
            eula.ReadOnly = true;
            eula.ScrollBars = ScrollBars.Vertical;
            eula.Font = new Font("Consolas", 9f * (float)s);
            eula.BackColor = Color.White;
            eula.Text = Setup.ReadResource(Str.IsChinese ? "EULA.txt" : "EULA-en.txt")
                            .Replace("\n", "\r\n").Replace("\r\r", "\r");
            eula.SetBounds(Px(16), Px(84), Px(608), Px(370));
            eula.Select(0, 0);
            Controls.Add(eula);

            agree.Text = Str.T("我已完整閱讀並同意上述使用條款與免責聲明",
                "I have read and accept the licence and disclaimer above");
            agree.SetBounds(Px(16), Px(464), Px(560), Px(26));
            agree.CheckedChanged += delegate { install.Enabled = agree.Checked; };
            Controls.Add(agree);

            desktop.Text = Str.T("在桌面建立捷徑", "Create a desktop shortcut");
            desktop.Checked = true;
            desktop.SetBounds(Px(16), Px(492), Px(300), Px(26));
            Controls.Add(desktop);

            pathLabel.Text = Str.T("安裝位置：", "Installs to: ") + Setup.InstallDir;
            pathLabel.ForeColor = Color.FromArgb(95, 95, 95);
            pathLabel.SetBounds(Px(16), Px(522), Px(610), Px(24));
            Controls.Add(pathLabel);

            install.Text = Str.T("安裝", "Install");
            install.Enabled = false;
            install.SetBounds(Px(410), Px(552), Px(100), Px(32));
            install.Click += delegate { DoInstall(); };
            Controls.Add(install);

            var cancel = new Button();
            cancel.Text = Str.T("取消", "Cancel");
            cancel.SetBounds(Px(520), Px(552), Px(100), Px(32));
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        int Px(int logical) { return (int)Math.Round(logical * s); }

        static double Scale()
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

        void DoInstall()
        {
            install.Enabled = false;
            try
            {
                Setup.Install(desktop.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Str.T("安裝失敗：", "Installation failed: ") + ex.Message, "SCMWC",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = true;
                return;
            }

            if (MessageBox.Show(Str.T("安裝完成。\r\n\r\n要現在啟動 SCMWC 嗎？",
                    "Installed.\r\n\r\nStart SCMWC now?"),
                    "SCMWC", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                == DialogResult.Yes)
            {
                try { Process.Start(Path.Combine(Setup.InstallDir, "SCMWC.exe")); }
                catch (Exception) { }
            }
            Close();
        }
    }
}
