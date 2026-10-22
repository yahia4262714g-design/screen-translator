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

            // Start of each word = width of everything before it (including the space).
            var starts = new int[words.Count];
            string prefix = "";
            for (int i = 0; i < words.Count; i++)
            {
                starts[i] = prefix.Length == 0 ? 0 : Width(prefix + " " + words[i], font) - Width(words[i], font);
                prefix = prefix.Length == 0 ? words[i] : prefix + " " + words[i];
            }

            int space = Width("a a", font) - Width("aa", font);
            for (int i = 0; i < words.Count; i++)
            {
                // A word ends where the space before the next one begins.
                int end = i + 1 < words.Count ? starts[i + 1] - space : starts[i] + Width(words[i], font);
                Items.Add(new Item
                {
                    Text = words[i],
                    Lookup = CleanWord(words[i]),
                    Rect = new Rectangle(left + starts[i], y, Math.Max(1, end - starts[i]), lineHeight)
                });
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
            Line hoverLine = null;
            foreach (var line in _lines)
            {
                var origin = new Point(line.Origin.X, line.Origin.Y + offsetY);
                TextRenderer.DrawText(g, line.Text, font, origin, color, Flags | TextFormatFlags.PreserveGraphicsClipping);
                if (hover != null && line.Origin.Y == hover.Rect.Y) hoverLine = line;
            }

            if (hover == null || hoverLine == null) return;

            // Redraw the same line in the accent colour, clipped to the hovered word, so the
            // glyphs land on exactly the same pixels.
            var r = hover.Rect;
            r.Offset(0, offsetY);
            var saved = g.Clip;
            g.SetClip(r, System.Drawing.Drawing2D.CombineMode.Intersect);
            using (var bg = new SolidBrush(back))
                g.FillRectangle(bg, r);
            TextRenderer.DrawText(g, hoverLine.Text, font, new Point(hoverLine.Origin.X, hoverLine.Origin.Y + offsetY), hoverColor,
                Flags | TextFormatFlags.PreserveGraphicsClipping);
            g.Clip = saved;

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
