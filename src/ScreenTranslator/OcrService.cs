using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator
{
    internal sealed class OcrWordBox
    {
        public RectangleF Bounds;
        public string Text;
    }

    internal sealed class OcrTextLine
    {
        public RectangleF Bounds;
        public string Text;
        public readonly List<OcrWordBox> Words = new List<OcrWordBox>();
    }

    internal sealed class OcrUnavailableException : Exception
    {
        public OcrUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads text from an image with the OCR engine built into Windows (free, offline).
    /// </summary>
    internal static class OcrService
    {
        private static OcrEngine _engine;

        public static OcrEngine GetEngine()
        {
            if (_engine != null) return _engine;

            var preferred = new Language(Config.OcrLanguageTag);
            if (OcrEngine.IsLanguageSupported(preferred))
            {
                _engine = OcrEngine.TryCreateFromLanguage(preferred);
            }

            if (_engine == null)
            {
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                {
                    if (lang.LanguageTag.StartsWith(Config.SourceLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        _engine = OcrEngine.TryCreateFromLanguage(lang);
                        if (_engine != null) break;
                    }
                }
            }

            if (_engine == null)
            {
                throw new OcrUnavailableException(
                    "قارئ النصوص الإنجليزي (OCR) مش مثبّت على ويندوز.\n" +
                    "ثبّته من: الإعدادات ← الوقت واللغة ← اللغة والمنطقة ← إضافة لغة ← English (United States).");
            }

            return _engine;
        }

        public static string AvailableLanguagesDescription()
        {
            var tags = new List<string>();
            foreach (var lang in OcrEngine.AvailableRecognizerLanguages) tags.Add(lang.LanguageTag);
            return tags.Count == 0 ? "(none)" : string.Join(", ", tags);
        }

        /// <summary>
        /// Returns the recognised lines with their positions in <paramref name="image"/> pixels.
        /// </summary>
        public static async Task<List<OcrTextLine>> RecognizeAsync(Bitmap image)
        {
            var engine = GetEngine();

            // Windows OCR reads small UI text far better when it is enlarged.
            int longest = Math.Max(image.Width, image.Height);
            float scale = Math.Min(2f, OcrEngine.MaxImageDimension / (float)longest);

            OcrResult result;
            using (var prepared = Resize(image, scale))
            using (var softwareBitmap = ToSoftwareBitmap(prepared))
            {
                result = await engine.RecognizeAsync(softwareBitmap).AsTask().ConfigureAwait(false);
            }

            var lines = new List<OcrTextLine>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text)) continue;

                var textLine = new OcrTextLine { Text = line.Text.Trim() };
                float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    left = Math.Min(left, (float)r.X);
                    top = Math.Min(top, (float)r.Y);
                    right = Math.Max(right, (float)(r.X + r.Width));
                    bottom = Math.Max(bottom, (float)(r.Y + r.Height));
                    textLine.Words.Add(new OcrWordBox
                    {
                        Bounds = new RectangleF((float)r.X / scale, (float)r.Y / scale, (float)r.Width / scale, (float)r.Height / scale),
                        Text = word.Text
                    });
                }

                textLine.Bounds = RectangleF.FromLTRB(left / scale, top / scale, right / scale, bottom / scale);
                lines.Add(textLine);
            }
            return lines;
        }

        private static Bitmap Resize(Bitmap source, float scale)
        {
            int w = Math.Max(1, (int)Math.Round(source.Width * scale));
            int h = Math.Max(1, (int)Math.Round(source.Height * scale));
            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, w, h));
            }
            return result;
        }

        private static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[w * h * 4];
            try
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * w * 4, w * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            // GDI+ 32bpp ARGB is laid out as B,G,R,A in memory, which is exactly Bgra8.
            return SoftwareBitmap.CreateCopyFromBuffer(
                pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        }
    }
}
