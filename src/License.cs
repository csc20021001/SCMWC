using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ScreenWatch
{
    public enum LicenseState
    {
        Free,           // 沒有授權碼
        Active,         // 已啟用，最近驗證過
        GracePeriod,    // 已啟用但連不上伺服器，還在寬限期內
        Expired,        // 寬限期用完，或伺服器說這組授權已失效
        Checking
    }

    /// <summary>
    /// Lemon Squeezy 授權碼。
    ///
    /// Lemon Squeezy 只有線上驗證，沒有離線憑證也沒有寬限期機制。
    /// 直接照它的設計走的話，使用者一斷網就變成沒授權 —— 對一個很可能
    /// 裝在內網機器上的工具來說不能接受。所以這裡自己補一層：
    /// 啟用成功後把結果簽章存在本機，之後離線就驗本地簽章，
    /// 每隔幾天才連線重新確認一次。
    /// </summary>
    public static class License
    {
        const string DefaultApiBase = "https://api.lemonsqueezy.com/v1/licenses/";

        /// <summary>可以覆寫成別的端點，測試用；平常不會出現在設定檔裡。</summary>
        static string ApiBase
        {
            get { return Config.Get("license.apiBase", DefaultApiBase); }
        }
        const int RecheckDays = 3;      // 幾天連線重新確認一次
        const int GraceDays = 14;       // 連不上時還能撐幾天

        public static LicenseState State = LicenseState.Free;
        public static string LastError = "";

        public static bool IsPro
        {
            get { return State == LicenseState.Active || State == LicenseState.GracePeriod; }
        }

        public static string Key { get { return Config.Get("license.key", ""); } }

        // ------------------------------------------------------------------
        // 本機狀態
        // ------------------------------------------------------------------

        /// <summary>這台機器的識別碼。用來讓一組授權碼只綁定固定幾台電腦。</summary>
        public static string DeviceId()
        {
            string guid = "";
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                           RegistryView.Registry64)
                       .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (k != null) guid = Convert.ToString(k.GetValue("MachineGuid", ""));
                }
            }
            catch (Exception) { }
            if (guid.Length == 0) guid = Environment.MachineName;
            return Hash(guid + "|" + Environment.UserName).Substring(0, 16);
        }

        static string Hash(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 本機快取的簽章。綁上 DeviceId，所以把 config.txt 複製到別台電腦不會生效。
        ///
        /// 這是一般 utility 的防護等級：擋得住隨手複製，擋不住認真的破解者。
        /// 認真要防的話得靠伺服器端，那是另一個量級的工程。
        /// </summary>
        static string Sign(string payload)
        {
            byte[] key = Encoding.UTF8.GetBytes("ScreenWatch/0.1|" + DeviceId());
            using (var h = new HMACSHA256(key))
                return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        static string Payload()
        {
            return string.Join("|", new string[] {
                Config.Get("license.key", ""),
                Config.Get("license.instance", ""),
                Config.Get("license.lastCheckUtc", ""),
                Config.Get("license.status", "")
            });
        }

        /// <summary>給測試用：重算目前設定內容的簽章。</summary>
        public static string SignCurrent() { return Sign(Payload()); }

        static void SaveLocal(string status)
        {
            Config.Set("license.status", status);
            Config.Set("license.lastCheckUtc", DateTime.UtcNow.ToString("o"));
            Config.Set("license.sig", Sign(Payload()));
            Config.Save();
        }

        static void ClearLocal()
        {
            Config.Set("license.key", "");
            Config.Set("license.instance", "");
            Config.Set("license.status", "");
            Config.Set("license.lastCheckUtc", "");
            Config.Set("license.sig", "");
            Config.Save();
        }

        /// <summary>啟動時呼叫。只看本機資料，不連網。</summary>
        public static void LoadLocal()
        {
            State = LicenseState.Free;
            if (Key.Length == 0) return;

            if (Config.Get("license.sig", "") != Sign(Payload()))
            {
                LastError = Str.T("本機的授權資料被改過或來自別台電腦",
                    "The stored licence was modified, or came from another computer");
                State = LicenseState.Expired;
                return;
            }
            if (Config.Get("license.status", "") != "active")
            {
                State = LicenseState.Expired;
                return;
            }

            DateTime last;
            if (!DateTime.TryParse(Config.Get("license.lastCheckUtc", ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last))
            {
                State = LicenseState.Expired;
                return;
            }
            double days = (DateTime.UtcNow - last.ToUniversalTime()).TotalDays;
            if (days > GraceDays) State = LicenseState.Expired;
            else if (days > RecheckDays) State = LicenseState.GracePeriod;
            else State = LicenseState.Active;
        }

        /// <summary>該不該趁現在連線重新確認一次。</summary>
        public static bool NeedsRecheck()
        {
            if (Key.Length == 0) return false;
            DateTime last;
            if (!DateTime.TryParse(Config.Get("license.lastCheckUtc", ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last)) return true;
            return (DateTime.UtcNow - last.ToUniversalTime()).TotalDays > RecheckDays;
        }

        // ------------------------------------------------------------------
        // 伺服器往返
        // ------------------------------------------------------------------

        /// <summary>啟用一組授權碼。成功回傳 true。</summary>
        public static bool Activate(string licenseKey)
        {
            LastError = "";
            licenseKey = (licenseKey ?? "").Trim();
            if (licenseKey.Length < 8)
            {
                LastError = Str.T("授權碼看起來不完整", "That licence key looks incomplete");
                return false;
            }

            string body = "license_key=" + Uri.EscapeDataString(licenseKey)
                        + "&instance_name=" + Uri.EscapeDataString(
                              Environment.MachineName + " (" + DeviceId() + ")");
            string json = Post("activate", body);
            if (json == null) return false;

            if (!JsonBool(json, "activated"))
            {
                LastError = JsonString(json, "error");
                if (LastError.Length == 0) LastError = Str.T("啟用被拒絕", "Activation was refused");
                return false;
            }

            string instance = JsonString(json, "id");
            Config.Set("license.key", licenseKey);
            Config.Set("license.instance", instance);
            SaveLocal("active");
            State = LicenseState.Active;
            return true;
        }

        /// <summary>重新確認授權還有效。連不上時不會清掉授權，交給寬限期處理。</summary>
        public static void Validate()
        {
            if (Key.Length == 0) return;
            LastError = "";
            string body = "license_key=" + Uri.EscapeDataString(Key)
                        + "&instance_id=" + Uri.EscapeDataString(
                              Config.Get("license.instance", ""));
            string json = Post("validate", body);
            if (json == null)
            {
                // 連不上 ≠ 沒授權。維持現狀，寬限期到了自然會降級。
                LoadLocal();
                return;
            }
            bool valid = JsonBool(json, "valid");
            string status = JsonString(json, "status");
            if (valid && status == "active")
            {
                SaveLocal("active");
                State = LicenseState.Active;
            }
            else
            {
                SaveLocal("inactive");
                State = LicenseState.Expired;
                LastError = status.Length > 0
                    ? Str.T("授權狀態：", "Licence status: ") + status
                    : Str.T("授權已失效", "This licence is no longer valid");
            }
        }

        /// <summary>在這台電腦停用，把名額還回去。</summary>
        public static bool Deactivate()
        {
            if (Key.Length == 0) return true;
            string body = "license_key=" + Uri.EscapeDataString(Key)
                        + "&instance_id=" + Uri.EscapeDataString(
                              Config.Get("license.instance", ""));
            string json = Post("deactivate", body);
            ClearLocal();
            State = LicenseState.Free;
            if (json == null)
            {
                LastError = Str.T("本機已停用，但沒連上伺服器，名額可能要等下次才會釋出",
                    "Deactivated locally, but the server was unreachable — the seat may free up later");
                return false;
            }
            return true;
        }

        static string Post(string endpoint, string body)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
                var req = (HttpWebRequest)WebRequest.Create(ApiBase + endpoint);
                req.Method = "POST";
                req.Accept = "application/json";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "ScreenWatch/0.1";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                    return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                // Lemon Squeezy 用 400 回報「授權碼錯誤」，那也是有意義的答案
                var resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    try
                    {
                        using (var r = new StreamReader(resp.GetResponseStream()))
                            return r.ReadToEnd();
                    }
                    catch (Exception) { }
                }
                LastError = Str.T("連不上授權伺服器", "Cannot reach the licence server");
                return null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        // ------------------------------------------------------------------
        // 極簡 JSON 讀取
        //
        // 只為了取幾個固定欄位，不值得為此打包一套 JSON 函式庫
        // （會讓 100 KB 的安裝檔變大好幾倍）。
        // ------------------------------------------------------------------

        static string JsonString(string json, string key)
        {
            string pat = "\"" + key + "\":";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            while (i >= 0)
            {
                int j = i + pat.Length;
                while (j < json.Length && json[j] == ' ') j++;
                if (j < json.Length && json[j] == '"')
                {
                    j++;
                    var sb = new StringBuilder();
                    while (j < json.Length && json[j] != '"')
                    {
                        if (json[j] == '\\' && j + 1 < json.Length) j++;
                        sb.Append(json[j++]);
                    }
                    return sb.ToString();
                }
                i = json.IndexOf(pat, i + 1, StringComparison.Ordinal);
            }
            return "";
        }

        static bool JsonBool(string json, string key)
        {
            string pat = "\"" + key + "\":";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return false;
            int j = i + pat.Length;
            while (j < json.Length && json[j] == ' ') j++;
            return string.CompareOrdinal(json, j, "true", 0, 4) == 0;
        }
    }
}
