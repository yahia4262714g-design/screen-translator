using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenTranslator
{
    internal sealed class TranslationException : Exception
    {
        public TranslationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Free translation: Google Translate's public endpoint, with MyMemory as a backup.
    /// </summary>
    internal static class Translator
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            return client;
        }

        /// <summary>Translates every block, a few at a time, and stores the result in <see cref="TextBlock.Translation"/>.</summary>
        public static async Task TranslateBlocksAsync(IList<TextBlock> blocks)
        {
            using (var gate = new SemaphoreSlim(4))
            {
                var tasks = new List<Task>();
                foreach (var block in blocks)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync().ConfigureAwait(false);
                        try { block.Translation = await TranslateAsync(block.Text).ConfigureAwait(false); }
                        finally { gate.Release(); }
                    }));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        public static async Task<string> TranslateAsync(string text)
        {
            try
            {
                return await GoogleAsync(text).ConfigureAwait(false);
            }
            catch (Exception googleError)
            {
                try
                {
                    return await MyMemoryAsync(text).ConfigureAwait(false);
                }
                catch
                {
                    throw new TranslationException("تعذّر الاتصال بخدمة الترجمة. تأكد من الإنترنت وجرّب مرة ثانية.", googleError);
                }
            }
        }

        private static async Task<string> GoogleAsync(string text)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl="
                         + Config.SourceLanguage + "&tl=" + Config.TargetLanguage;
            var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) });

            using (var response = await Http.PostAsync(url, body).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Shape: [[["translated","original",...], ["translated","original",...]], ...]
                var root = new JavaScriptSerializer().DeserializeObject(json) as object[];
                var sentences = root?[0] as object[];
                if (sentences == null) throw new InvalidOperationException("Unexpected Google response.");

                var sb = new StringBuilder();
                foreach (var sentence in sentences)
                {
                    if (sentence is object[] parts && parts.Length > 0 && parts[0] is string piece)
                        sb.Append(piece);
                }

                string result = sb.ToString().Trim();
                if (result.Length == 0) throw new InvalidOperationException("Empty Google response.");
                return result;
            }
        }

        private static async Task<string> MyMemoryAsync(string text)
        {
            // MyMemory accepts up to 500 bytes per request, so long text goes in sentence-sized chunks.
            var sb = new StringBuilder();
            foreach (string chunk in SplitIntoChunks(text, 450))
            {
                string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(chunk)
                             + "&langpair=" + Config.SourceLanguage + "%7C" + Config.TargetLanguage;
                string json = await Http.GetStringAsync(url).ConfigureAwait(false);

                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                var data = root?["responseData"] as Dictionary<string, object>;
                string piece = data?["translatedText"] as string;

                bool quotaHit = root != null && root.TryGetValue("quotaFinished", out var q) && q is bool b && b;
                if (string.IsNullOrWhiteSpace(piece) || quotaHit || piece.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("MyMemory unavailable.");

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(WebUtility.HtmlDecode(piece).Trim());
            }
            return sb.ToString();
        }

        private static IEnumerable<string> SplitIntoChunks(string text, int maxLength)
        {
            var current = new StringBuilder();
            foreach (string word in text.Split(' '))
            {
                if (current.Length > 0 && current.Length + word.Length + 1 > maxLength)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            if (current.Length > 0) yield return current.ToString();
        }
    }
}
