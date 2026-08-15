using System;
using System.Globalization;

namespace ScreenWatch
{
    /// <summary>
    /// 介面語言。
    ///
    /// 刻意不做 key/資源檔那一套：這個產品的字串量（約 190 條）用 key 表
    /// 反而更難維護 —— 改文案時要在兩個檔案之間跳，而且很容易留下沒人用的孤兒 key。
    /// 直接在呼叫處把兩種語言並列，翻譯永遠跟著使用它的程式碼走。
    ///
    ///     Str.T("監視中", "Watching")
    ///
    /// 代價是之後要加第三種語言會很痛。真的走到那一步再換成資源檔。
    /// </summary>
    public static class Str
    {
        static bool chinese = true;

        public static bool IsChinese { get { return chinese; } }

        /// <summary>啟動時呼叫。設定檔沒指定的話，跟著 Windows 的顯示語言走。</summary>
        public static void Init()
        {
            string pref = Config.Get("ui.language", "auto");
            if (pref == "zh") { chinese = true; return; }
            if (pref == "en") { chinese = false; return; }
            chinese = DetectChinese();
        }

        static bool DetectChinese()
        {
            try
            {
                string name = CultureInfo.CurrentUICulture.Name;
                if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true;
                string parent = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return string.Equals(parent, "zh", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>切換語言並記住。介面要重建才會全部換過來。</summary>
        public static void Set(bool useChinese)
        {
            chinese = useChinese;
            Config.Set("ui.language", useChinese ? "zh" : "en");
            Config.Save();
        }

        public static string T(string zh, string en) { return chinese ? zh : en; }
    }
}
