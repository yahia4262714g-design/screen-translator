using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// "ScreenTranslator.exe --selftest out.txt [previewDir]" checks OCR, translation, the word
    /// dictionary and text clean-up, writes a report, and (with previewDir) renders PNG previews
    /// of the overlay and popups so the UI can be checked without a Windows desktop. Used by CI.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string reportPath, string previewDir)
        {
            var report = new StringBuilder();
            bool ok = true;
            void Check(string name, bool passed, string detail)
            {
                report.AppendLine((passed ? "PASS " : "FAIL ") + name + ": " + detail);
                ok &= passed;
            }

            try
            {
                report.AppendLine("OCR languages: " + OcrService.AvailableLanguagesDescription());

                // 1. OCR + paragraph grouping + translation.
                using (var bmp = new Bitmap(760, 220))
                using (var g = Graphics.FromImage(bmp))
                using (var title = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var body = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString("Welcome to the screen translator", title, Brushes.Black, 20, 20);
                    g.DrawString("Select any area on your screen and the English text", body, Brushes.Black, 20, 90);
                    g.DrawString("inside it will be translated into Arabic instantly.", body, Brushes.Black, 20, 113);

                    var lines = OcrService.RecognizeAsync(bmp).GetAwaiter().GetResult();
                    foreach (var l in lines) report.AppendLine("  OCR line: " + l.Text + "  @ " + l.Bounds + "  words=" + l.Words.Count);

                    var blocks = TextLayout.GroupIntoBlocks(lines);
                    Translator.TranslateBlocksAsync(blocks).GetAwaiter().GetResult();
                    foreach (var b in blocks) report.AppendLine("  EN: " + b.Text + "\r\n  AR: " + b.Translation);

                    string all = string.Join(" ", lines.ConvertAll(l => l.Text));
                    Check("ocr", all.IndexOf("translator", StringComparison.OrdinalIgnoreCase) >= 0, all);
                    Check("paragraphs", blocks.Count == 2, blocks.Count + " blocks");
                    Check("translation", blocks.Count > 0 && blocks.TrueForAll(b => HasArabic(b.Translation)), "arabic text returned");
                    Check("word boxes", lines.Count > 0 && lines[0].Words.Count == 5, lines.Count > 0 ? lines[0].Words.Count + " words" : "none");
                }

                // 2. Dictionary lookup for one word.
                var info = Translator.LookupWordAsync("translate").GetAwaiter().GetResult();
                report.AppendLine("  word: " + info.Word + " /" + info.Phonetic + "/ = " + info.Translation);
                foreach (var m in info.Meanings) report.AppendLine("    " + m.PartOfSpeech + ": " + string.Join("، ", m.Terms));
                Check("dictionary", HasArabic(info.Translation) && info.Meanings.Count > 0, info.Meanings.Count + " parts of speech");

                // 3. Text copied from a PDF: broken lines are re-joined, paragraphs kept.
                string pdf = "This sentence was broken\nacross two lines.\n\nSecond paragraph starts here.";
                string normalized = SelectedText.Normalize(pdf);
                Check("pdf clean-up", normalized == "This sentence was broken across two lines.\n\nSecond paragraph starts here.",
                    normalized.Replace("\n", "\\n"));

                Check("clean word", WordFlow.CleanWord("“Hello,”") == "hello" && WordFlow.CleanWord("API.") == "API" &&
                                    WordFlow.CleanWord("—") == null, "punctuation stripped");

                if (!string.IsNullOrEmpty(previewDir)) RenderPreviews(previewDir, report);
            }
            catch (Exception ex)
            {
                Check("exception", false, ex.ToString());
            }

            report.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
            return ok ? 0 : 2;
        }

        private static bool HasArabic(string s)
        {
            foreach (char c in s ?? "") if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        // ---------- UI previews ----------

        private static void RenderPreviews(string dir, StringBuilder report)
        {
            Directory.CreateDirectory(dir);

            // A small "web page" to translate in place.
            using (var page = SamplePage())
            {
                var lines = OcrService.RecognizeAsync(page).GetAwaiter().GetResult();
                var blocks = TextLayout.GroupIntoBlocks(lines);
                Translator.TranslateBlocksAsync(blocks).GetAwaiter().GetResult();

                page.Save(Path.Combine(dir, "1-original.png"), ImageFormat.Png);

                using (var overlay = new OverlayForm(new Rectangle(0, 0, page.Width, page.Height), page))
                {
                    overlay.ShowTranslation(blocks);
                    SaveComposite(page, overlay, Path.Combine(dir, "2-overlay-translation.png"));
                    overlay.PreviewOriginal(1);
                    SaveComposite(page, overlay, Path.Combine(dir, "3-overlay-hover-original.png"));
                }
                report.AppendLine("  preview: overlay with " + blocks.Count + " blocks");
            }

            string text = "Learning a new language takes time, but reading real content every day makes a huge difference. " +
                          "Highlight any sentence and the translation appears instantly.";
            using (var popup = new TextPopup(text, new Point(80, 80)))
            {
                popup.SetTranslation(Translator.TranslateAsync(text).GetAwaiter().GetResult());
                SaveForm(popup, Path.Combine(dir, "4-selected-text-popup.png"));
            }

            Translator.LookupWordAsync("difference").GetAwaiter().GetResult(); // warm the cache
            using (var card = new WordCard("difference", text, new Rectangle(80, 80, 60, 20), speak: false))
                SaveForm(card, Path.Combine(dir, "5-word-card.png"));
        }

        private static Bitmap SamplePage()
        {
            var bmp = new Bitmap(720, 300, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var h1 = new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var p = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var btn = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var blue = new SolidBrush(Color.FromArgb(37, 99, 235)))
            using (var gray = new SolidBrush(Color.FromArgb(55, 65, 81)))
            {
                g.Clear(Color.FromArgb(249, 250, 251));
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.DrawString("Build faster with better tools", h1, Brushes.Black, 32, 30);
                g.DrawString("Our platform helps small teams design, test and ship products", p, gray, 32, 92);
                g.DrawString("without the usual complexity. Start your free trial today.", p, gray, 32, 116);
                g.FillRectangle(blue, 32, 180, 170, 46);
                g.DrawString("Get started now", btn, Brushes.White, 54, 192);
            }
            return bmp;
        }

        private static void SaveForm(Form form, string path)
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void SaveComposite(Bitmap page, Form overlay, string path)
        {
            using (var layer = new Bitmap(overlay.Width, overlay.Height))
            using (var result = new Bitmap(Math.Max(page.Width, overlay.Width), Math.Max(page.Height, overlay.Height)))
            using (var g = Graphics.FromImage(result))
            {
                overlay.DrawToBitmap(layer, new Rectangle(0, 0, overlay.Width, overlay.Height));
                layer.MakeTransparent(overlay.TransparencyKey);
                g.Clear(Color.White);
                g.DrawImageUnscaled(page, 0, 0);
                g.DrawImageUnscaled(layer, 0, 0);
                result.Save(path, ImageFormat.Png);
            }
        }
    }
}
