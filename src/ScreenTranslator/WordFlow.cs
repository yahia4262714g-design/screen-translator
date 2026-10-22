using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Lays out English text word by word so every word can be hovered and clicked.
    /// </summary>
    internal sealed class WordFlow
    {
        internal sealed class Item
        {
            public string Text;
            public string Lookup;   // the word without punctuation, or null when not a word
            public Rectangle Rect;
        }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public readonly List<Item> Items = new List<Item>();
        public int Height;

        public static WordFlow Build(string text, Font font, int left, int top, int maxWidth, int lineGap)
        {
            var flow = new WordFlow();
            int space = TextRenderer.MeasureText("a a", font, Size.Empty, Flags).Width
                        - TextRenderer.MeasureText("aa", font, Size.Empty, Flags).Width;
            int lineHeight = TextRenderer.MeasureText("Ag", font, Size.Empty, Flags).Height;

            int x = left, y = top;
            bool lineHasWords = false;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                if (li > 0)
                {
                    // An empty line marks a paragraph: leave a little extra room.
                    y += lineHasWords ? lineHeight + lineGap : lineHeight / 2;
                    x = left;
                    lineHasWords = false;
                }

                foreach (string token in lines[li].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int w = TextRenderer.MeasureText(token, font, Size.Empty, Flags).Width;
                    if (lineHasWords && x + w > left + maxWidth)
                    {
                        x = left;
                        y += lineHeight + lineGap;
                    }
                    flow.Items.Add(new Item { Text = token, Lookup = CleanWord(token), Rect = new Rectangle(x, y, w, lineHeight) });
                    x += w + space;
                    lineHasWords = true;
                }
            }
            flow.Height = (lineHasWords ? y + lineHeight : y) - top;
            return flow;
        }

        public Item HitTest(Point p)
        {
            foreach (var item in Items)
            {
                if (item.Lookup != null && Rectangle.Inflate(item.Rect, 2, 2).Contains(p)) return item;
            }
            return null;
        }

        public void Draw(Graphics g, Font font, Color color, Color hoverColor, Item hover, int offsetY, int underline)
        {
            foreach (var item in Items)
            {
                var r = item.Rect;
                r.Offset(0, offsetY);
                bool isHover = item == hover;
                TextRenderer.DrawText(g, item.Text, font, r.Location, isHover ? hoverColor : color,
                    Flags | TextFormatFlags.PreserveGraphicsClipping);
                if (isHover)
                {
                    using (var brush = new SolidBrush(hoverColor))
                        g.FillRectangle(brush, r.Left, r.Bottom - underline, r.Width, underline);
                }
            }
        }

        /// <summary>"translator," → "translator"; "Hello!" → "hello"; "—" → null.</summary>
        public static string CleanWord(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            int start = 0, end = token.Length - 1;
            while (start <= end && !char.IsLetterOrDigit(token[start])) start++;
            while (end >= start && !char.IsLetterOrDigit(token[end])) end--;
            if (start > end) return null;

            string word = token.Substring(start, end - start + 1);
            if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("’s", StringComparison.OrdinalIgnoreCase))
                word = word.Substring(0, word.Length - 2);

            bool hasLetter = false;
            foreach (char c in word) if (char.IsLetter(c)) { hasLetter = true; break; }
            if (!hasLetter) return null;

            // Keep acronyms (NASA, API) as they are, lower-case everything else.
            bool allUpper = word.Length > 1 && word.ToUpperInvariant() == word;
            return allUpper ? word : word.ToLowerInvariant();
        }
    }
}
