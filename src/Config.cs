using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScreenWatch
{
    /// <summary>
    /// 極簡設定檔：%LOCALAPPDATA%\ScreenWatch\config.txt，一行一個 key=value。
    /// 目前只存 Discord 設定；之後要存監視項目也放這裡。
    /// </summary>
    public static class Config
    {
        static readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public static string Path
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ScreenWatch");
                return System.IO.Path.Combine(dir, "config.txt");
            }
        }

        public static void Load()
        {
            values.Clear();
            try
            {
                if (!File.Exists(Path)) return;
                foreach (string line in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int i = t.IndexOf('=');
                    if (i <= 0) continue;
                    values[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
                }
            }
            catch (Exception) { }
        }

        public static void Save()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("# ScreenWatch settings");
                foreach (var kv in values) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception) { }
        }

        public static string Get(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            values[key] = value == null ? "" : value;
        }

        public static bool GetBool(string key, bool fallback)
        {
            string v = Get(key, null);
            if (v == null) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }

        public static string DiscordWebhook
        {
            get { return Get("discord.webhook", ""); }
            set { Set("discord.webhook", value); }
        }

        /// <summary>
        /// 附上畫面截圖預設關閉：那等於把螢幕內容上傳到外部服務，
        /// 必須由使用者自己明確打開。
        /// </summary>
        public static bool DiscordAttachImage
        {
            get { return GetBool("discord.attachImage", false); }
            set { SetBool("discord.attachImage", value); }
        }
    }
}
