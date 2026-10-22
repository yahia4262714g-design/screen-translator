using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text;

namespace ScreenTranslator
{
    /// <summary>
    /// "ScreenTranslator.exe --selftest out.txt" renders English text into an image,
    /// runs it through OCR + translation and writes a report. Used by CI.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string reportPath)
        {
            var report = new StringBuilder();
            int exitCode = 1;
            try
            {
                report.AppendLine("OCR languages: " + OcrService.AvailableLanguagesDescription());

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
                    foreach (var l in lines) report.AppendLine("OCR line: " + l.Text + "  @ " + l.Bounds);

                    var blocks = TextLayout.GroupIntoBlocks(lines);
                    report.AppendLine("Blocks: " + blocks.Count);

                    Translator.TranslateBlocksAsync(blocks).GetAwaiter().GetResult();
                    bool arabic = false;
                    foreach (var b in blocks)
                    {
                        report.AppendLine("EN: " + b.Text);
                        report.AppendLine("AR: " + b.Translation);
                        foreach (char c in b.Translation ?? "") if (c >= '؀' && c <= 'ۿ') arabic = true;
                    }

                    bool readEnglish = string.Join(" ", lines.ConvertAll(l => l.Text)).IndexOf("translator", StringComparison.OrdinalIgnoreCase) >= 0;
                    report.AppendLine("Result: ocr=" + readEnglish + " translation=" + arabic + " blocks=" + blocks.Count);
                    exitCode = readEnglish && arabic && blocks.Count == 2 ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("ERROR: " + ex);
            }

            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
            return exitCode;
        }
    }
}
