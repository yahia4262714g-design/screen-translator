using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Lays out English text so every word can be hovered and clicked.
    /// Each line is drawn as one string (natural spacing); word positions are found by
    /// measuring the text before them, so hit-testing matches what is on screen.
    /// </summary>
    internal sealed class WordFlow
    {
        internal sealed class Item
        {
            public string Text;
            public string Lookup;   // the word without punctuation, or null when not a word
            public Rectangle Rect;
        }

        private sealed class Line
        {
            public string Text;
            public Point Origin;
        }

        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public readonly List<Item> Items = new List<Item>();
        private readonly List<Line> _lines = new List<Line>();
        public int Height;

        private static int Width(string s, Font font) =>
            s.Length == 0 ? 0 : TextRenderer.MeasureText(s, font, new Size(int.MaxValue, int.MaxValue), Flags).Width;

        public static WordFlow Build(string text, Font font, int left, int top, int maxWidth, int lineGap)
        {
            var flow = new WordFlow();
            int lineHeight = TextRenderer.MeasureText("Ag", font, new Size(int.MaxValue, int.MaxValue), Flags).Height;
            int y = top;
            bool first = true;

            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var words = paragraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    if (!first) y += lineHeight / 2; // blank line = paragraph gap
                    continue;
                }

                var current = new List<string>();
                foreach (string word in words)
                {
                    if (current.Count > 0 && Width(string.Join(" ", current) + " " + word, font) > maxWidth)
                    {
                        if (!first) y += lineGap;
                        flow.AddLine(current, font, left, y, lineHeight);
                        y += lineHeight;
                        first = false;
                        current.Clear();
                    }
                    current.Add(word);
                }
                if (!first) y += lineGap;
                flow.AddLine(current, font, left, y, lineHeight);
                y += lineHeight;
                first = false;
            }

            flow.Height = y - top;
            return flow;
        }

        private void AddLine(List<string> words, Font font, int left, int y, int lineHeight)
        {
            string lineText = string.Join(" ", words);
            _lines.Add(new Line { Text = lineText, Origin = new Point(left, y) });

            string prefix = "";
            foreach (string word in words)
            {
                // Position = width of everything before the word, including the space.
                int x = prefix.Length == 0 ? 0 : Width(prefix + " " + word, font) - Width(word, font);
                Items.Add(new Item
                {
                    Text = word,
                    Lookup = CleanWord(word),
                    Rect = new Rectangle(left + x, y, Width(word, font), lineHeight)
                });
                prefix = prefix.Length == 0 ? word : prefix + " " + word;
            }
        }

        public Item HitTest(Point p)
        {
            foreach (var item in Items)
            {
                if (item.Lookup != null && Rectangle.Inflate(item.Rect, 2, 2).Contains(p)) return item;
            }
            return null;
        }

        public void Draw(Graphics g, Font font, Color color, Color hoverColor, Color back, Item hover, int offsetY, int underline)
        {
            foreach (var line in _lines)
            {
                var origin = new Point(line.Origin.X, line.Origin.Y + offsetY);
                TextRenderer.DrawText(g, line.Text, font, origin, color, Flags | TextFormatFlags.PreserveGraphicsClipping);
            }

            if (hover == null) return;
            var r = hover.Rect;
            r.Offset(0, offsetY);
            using (var bg = new SolidBrush(back))
                g.FillRectangle(bg, Rectangle.Inflate(r, 1, 0));
            TextRenderer.DrawText(g, hover.Text, font, r.Location, hoverColor, Flags | TextFormatFlags.PreserveGraphicsClipping);
            using (var brush = new SolidBrush(hoverColor))
                g.FillRectangle(brush, r.Left, r.Bottom - underline, r.Width, underline);
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
