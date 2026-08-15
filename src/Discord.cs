using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace ScreenWatch
{
    /// <summary>
    /// 推播到 Discord webhook。電腦在跑東西時人可以出去吃飯，手機收通知。
    ///
    /// 一律在背景執行緒送出：網路卡住不能拖住監視迴圈。
    /// </summary>
    public static class Discord
    {
        /// <summary>最後一次送出的結果，給 UI 顯示。</summary>
        public static string LastResult = "";

        public static bool Configured
        {
            get
            {
                string u = Config.DiscordWebhook;
                return u.Length > 0 && (u.StartsWith("http://") || u.StartsWith("https://"));
            }
        }

        public static void Send(string text, Bitmap image)
        {
            if (!Configured)
            {
                if (image != null) image.Dispose();
                return;
            }
            string url = Config.DiscordWebhook;
            bool attach = Config.DiscordAttachImage && image != null;
            byte[] png = null;
            if (attach)
            {
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    png = ms.ToArray();
                }
            }
            if (image != null) image.Dispose();

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    LastResult = png != null ? PostMultipart(url, text, png) : PostJson(url, text);
                }
                catch (WebException ex)
                {
                    LastResult = Describe(ex);
                }
                catch (Exception ex)
                {
                    LastResult = Str.T("失敗：", "failed: ") + ex.Message;
                }
            });
        }

        static string Describe(WebException ex)
        {
            var r = ex.Response as HttpWebResponse;
            if (r == null) return Str.T("失敗：連不上網路或 Discord", "failed: cannot reach the network or Discord");
            string body = "";
            try
            {
                using (var s = new StreamReader(r.GetResponseStream()))
                    body = s.ReadToEnd();
            }
            catch (Exception) { }
            if ((int)r.StatusCode == 404) return Str.T("失敗：找不到這個 webhook，網址可能貼錯或已被刪除",
                "failed: webhook not found — the URL may be wrong or deleted");
            if ((int)r.StatusCode == 401 || (int)r.StatusCode == 403)
                return Str.T("失敗：這個 webhook 沒有權限，請重新建立一個",
                "failed: this webhook is not permitted — create a new one");
            if ((int)r.StatusCode == 429) return Str.T("失敗：傳太頻繁被 Discord 暫時擋下",
                "failed: rate-limited by Discord for sending too often");
            return string.Format(Str.T("失敗（代碼 {0}）{1}", "failed (code {0}) {1}"), (int)r.StatusCode,
                body.Length > 120 ? body.Substring(0, 120) : body);
        }

        static HttpWebRequest NewRequest(string url)
        {
            try
            {
                // Discord 需要 TLS 1.2；舊的 .NET Framework 預設值不一定夠。
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // .NET 預設會加 Expect: 100-continue，有些 webhook 端點／CDN 會擋。
                ServicePointManager.Expect100Continue = false;
            }
            catch (Exception) { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ServicePoint.Expect100Continue = false;
            req.UserAgent = "ScreenWatch/0.1";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            return req;
        }

        static string PostJson(string url, string text)
        {
            var req = NewRequest(url);
            req.ContentType = "application/json";
            byte[] body = Encoding.UTF8.GetBytes("{\"content\":\"" + JsonEscape(text) + "\"}");
            req.ContentLength = body.Length;
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
                return Str.T("成功", "sent");
        }

        static string PostMultipart(string url, string text, byte[] png)
        {
            string boundary = "----ScreenWatch" + DateTime.UtcNow.Ticks.ToString("x");
            var req = NewRequest(url);
            req.ContentType = "multipart/form-data; boundary=" + boundary;

            var head = new StringBuilder();
            head.Append("--").Append(boundary).Append("\r\n");
            head.Append("Content-Disposition: form-data; name=\"payload_json\"\r\n");
            head.Append("Content-Type: application/json\r\n\r\n");
            head.Append("{\"content\":\"").Append(JsonEscape(text)).Append("\"}\r\n");
            head.Append("--").Append(boundary).Append("\r\n");
            head.Append("Content-Disposition: form-data; name=\"files[0]\"; filename=\"change.png\"\r\n");
            head.Append("Content-Type: image/png\r\n\r\n");

            byte[] pre = Encoding.UTF8.GetBytes(head.ToString());
            byte[] post = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            req.ContentLength = pre.Length + png.Length + post.Length;
            using (var s = req.GetRequestStream())
            {
                s.Write(pre, 0, pre.Length);
                s.Write(png, 0, png.Length);
                s.Write(post, 0, post.Length);
            }
            using (var resp = (HttpWebResponse)req.GetResponse())
                return Str.T("成功（含截圖）", "sent with screenshot");
        }

        static string JsonEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
