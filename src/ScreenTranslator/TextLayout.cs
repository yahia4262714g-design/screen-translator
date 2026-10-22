using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenTranslator
{
    /// <summary>
    /// A paragraph (or heading, list item, label...) found on screen.
    /// Translated as one unit so sentences that wrap across lines keep their meaning.
    /// </summary>
    internal sealed class TextBlock
    {
        public readonly List<OcrTextLine> Lines = new List<OcrTextLine>();
        public RectangleF Bounds;
        public string Translation;

        public float AverageLineHeight
        {
            get
            {
                float sum = 0;
                foreach (var l in Lines) sum += l.Bounds.Height;
                return Lines.Count == 0 ? 0 : sum / Lines.Count;
            }
        }

        public void Add(OcrTextLine line)
        {
            Bounds = Lines.Count == 0 ? line.Bounds : RectangleF.Union(Bounds, line.Bounds);
            Lines.Add(line);
        }

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var line in Lines)
                {
                    string t = line.Text;
                    if (sb.Length == 0) { sb.Append(t); continue; }

                    // Re-join words hyphenated at the end of a line: "transla-" + "tion".
                    if (sb[sb.Length - 1] == '-' && t.Length > 0 && char.IsLower(t[0]))
                        sb.Length--;
                    else
                        sb.Append(' ');
                    sb.Append(t);
                }
                return sb.ToString();
            }
        }
    }

    internal static class TextLayout
    {
        private static readonly Regex ListItemStart = new Regex(@"^([•·▪◦●○■□➤►\-–—*]|\d{1,3}[.)])\s", RegexOptions.Compiled);

        public static List<TextBlock> GroupIntoBlocks(List<OcrTextLine> lines)
        {
            var blocks = new List<TextBlock>();
            TextBlock current = null;

            foreach (var line in lines)
            {
                if (current != null && !BelongsTo(current, line))
                    current = null;

                if (current == null)
                {
                    current = new TextBlock();
                    blocks.Add(current);
                }
                current.Add(line);
            }

            blocks.RemoveAll(b => !HasLetters(b.Text));
            return blocks;
        }

        private static bool BelongsTo(TextBlock block, OcrTextLine line)
        {
            var prev = block.Lines[block.Lines.Count - 1].Bounds;
            var cur = line.Bounds;

            float tall = Math.Max(prev.Height, cur.Height);
            float shortH = Math.Max(1f, Math.Min(prev.Height, cur.Height));
            float gap = cur.Top - prev.Bottom;

            bool closeBelow = gap > -tall * 0.5f && gap < tall * 0.8f;
            bool similarSize = tall / shortH < 1.6f;
            bool overlapsHorizontally = cur.Left < block.Bounds.Right && cur.Right > block.Bounds.Left;
            bool startsListItem = ListItemStart.IsMatch(line.Text);

            return closeBelow && similarSize && overlapsHorizontally && !startsListItem;
        }

        private static bool HasLetters(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }
    }
}
