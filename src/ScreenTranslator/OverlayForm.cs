using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Window placed exactly over the selected area. Each paragraph of the original text is
    /// covered by a patch in the same background colour with the translation written on it,
    /// so the translation appears "in place". Everything else stays see-through.
    ///
    /// Learning mode: resting the mouse on a patch shows the original English underneath;
    /// clicking a word there opens its dictionary card.
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private static readonly Color TransparentKey = Color.FromArgb(255, 0, 255);
        private static readonly Color Accent = Theme.Accent;
        private static readonly Color PillBack = Color.FromArgb(24, 26, 32);

        private const int PadX = 6;
        private const int PadY = 3;
        private const float MinFontPx = 11f;
        private const float MaxFontPx = 64f;
        private const int PeekDelayMs = 280;
        private const double BoldStrokeRatio = 0.13;

        private const TextFormatFlags TextFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

        private enum Mode { Loading, Result, Message }

        private sealed class Patch
        {
            public TextBlock Block;
            public Rectangle Rect;        // where the translation is drawn
            public Rectangle SourceRect;  // where the original text is
            public Rectangle TextRect;
            public string Text;
            public Font Font;
            public Color Back;
            public Color Fore;
            public TextFormatFlags Flags;
            public bool Bold;
            public double Stroke;
        }

        private readonly Rectangle _region;
        private readonly Bitmap _image;
        private readonly PixelData _pixels;
        private readonly List<Patch> _patches = new List<Patch>();
        private readonly ContextMenuStrip _menu;
        private readonly Timer _peekTimer;
        private readonly float _scale;
        private Mode _mode = Mode.Loading;
        private string _message;
        private string _originalText = "";
        private string _translatedText = "";
        private Timer _autoClose;

        private Point _cursorAtShow;
        private bool _mouseArmed;
        private Patch _hoverPatch, _peekPatch;
        private OcrWordBox _hoverWord;
        private WordCard _card;
        private bool _closeHover;

        public OverlayForm(Rectangle screenRegion, Bitmap regionImage)
        {
            _region = screenRegion;
            _image = regionImage;
            _pixels = new PixelData(regionImage);
            _scale = Theme.ScaleAt(screenRegion.Location);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = TransparentKey;
            TransparencyKey = TransparentKey;
            Bounds = screenRegion;

            _menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
            _menu.Items.Add("نسخ الترجمة", null, (s, e) => CopyToClipboard(_translatedText));
            _menu.Items.Add("نسخ النص الأصلي", null, (s, e) => CopyToClipboard(_originalText));
            _menu.Items.Add("استمع للنص الأصلي", null, (s, e) => Speech.Speak(_originalText));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("إغلاق  (Esc)", null, (s, e) => Close());
            ContextMenuStrip = _menu;

            _peekTimer = new Timer { Interval = PeekDelayMs };
            _peekTimer.Tick += (s, e) =>
            {
                _peekTimer.Stop();
                SetPeek(_hoverPatch);
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW; // keep it out of Alt+Tab
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE; // never steal focus: games stay in front
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _region;
        }

        /// <summary>Esc: closes the word card first, then the translation.</summary>
        public void HandleEscape()
        {
            if (CardOpen) CloseCard();
            else Close();
        }

        private Rectangle CloseButtonRect
        {
            get
            {
                int size = (int)Math.Round(26 * _scale);
                return new Rectangle(ClientSize.Width - size - 4, 4, size, size);
            }
        }

        private void DrawCloseButton(Graphics g)
        {
            var r = CloseButtonRect;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(_closeHover ? Color.FromArgb(196, 43, 28) : Color.FromArgb(24, 26, 32)))
                g.FillEllipse(bg, r);
            using (var border = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                g.DrawEllipse(border, r);
            int inset = r.Width / 3;
            using (var pen = new Pen(Color.White, Math.Max(1.5f, 1.6f * _scale)))
            {
                g.DrawLine(pen, r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset);
                g.DrawLine(pen, r.Right - inset, r.Top + inset, r.Left + inset, r.Bottom - inset);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        // ---------- public API ----------

        public void ShowTranslation(IList<TextBlock> blocks)
        {
            if (IsDisposed) return;

            var ordered = blocks.OrderBy(b => b.Bounds.Top).ToList();
            _originalText = string.Join(Environment.NewLine + Environment.NewLine, ordered.Select(b => b.Text));
            _translatedText = string.Join(Environment.NewLine + Environment.NewLine, ordered.Select(b => b.Translation));

            BuildPatches(ordered);
            _mode = Mode.Result;
            _cursorAtShow = Cursor.Position;
            Cursor = Cursors.Default;
            Invalidate();
        }

        public void ShowMessage(string message, bool autoClose)
        {
            if (IsDisposed) return;
            _message = message;
            _mode = Mode.Message;
            Invalidate();

            if (autoClose)
            {
                _autoClose = new Timer { Interval = 2600 };
                _autoClose.Tick += (s, e) => Close();
                _autoClose.Start();
            }
        }

        /// <summary>Self-test only: how each paragraph was styled.</summary>
        internal string DescribePatches() => string.Join("\r\n", _patches.Select(p =>
            "    [" + (p.Bold ? "bold" : "regular") + " stroke=" + p.Stroke.ToString("0.000") + " font=" +
            p.Font.Size.ToString("0.0") + "px] " + p.Block.Text));

        /// <summary>Self-test only: show the original of one paragraph with its first word hovered.</summary>
        internal void PreviewOriginal(int patchIndex)
        {
            if (patchIndex >= _patches.Count) return;
            _peekPatch = _patches[patchIndex];
            _hoverWord = _peekPatch.Block.Lines[0].Words.FirstOrDefault();
            Invalidate();
        }

        // ---------- layout ----------

        private void BuildPatches(List<TextBlock> blocks)
        {
            var imageBounds = new Rectangle(0, 0, _image.Width, _image.Height);
            int prevBottom = int.MinValue;
            int maxBottom = _region.Height;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (string.IsNullOrWhiteSpace(block.Translation)) continue;

                var source = Rectangle.Round(block.Bounds);
                source.Inflate(4, 3);
                source.Intersect(imageBounds);

                var rect = Rectangle.Round(block.Bounds);
                rect.Inflate(4, 3);

                // Very short labels get a bit more room for the (usually longer) translation.
                const int minWidth = 140;
                if (rect.Width < minWidth)
                {
                    int grow = minWidth - rect.Width;
                    if (Config.TargetIsRightToLeft) rect.X -= grow;
                    rect.Width += grow;
                }
                rect = ClampHorizontally(rect, _region.Width);

                // Labels and buttons (one line) are centred on the original; paragraphs follow the
                // reading direction.
                bool singleLine = block.Lines.Count == 1;
                if (singleLine)
                {
                    int centre = source.Left + source.Width / 2;
                    rect.X = Math.Max(0, Math.Min(_region.Width - rect.Width, centre - rect.Width / 2));
                }
                var flags = TextFlags | (singleLine
                    ? TextFormatFlags.HorizontalCenter | (Config.TargetIsRightToLeft ? TextFormatFlags.RightToLeft : 0)
                    : DirectionFlags);

                _pixels.SampleColors(Rectangle.Round(block.Bounds), out var back, out var fore);
                if (back.ToArgb() == TransparentKey.ToArgb()) back = Color.FromArgb(254, 0, 254);
                double stroke = _pixels.StrokeRatio(Rectangle.Round(block.Bounds), back, block.AverageLineHeight);
                bool bold = stroke >= BoldStrokeRatio;

                // Room for the translation: about the size of the original paragraph, never running
                // into the next one.
                int nextTop = i + 1 < blocks.Count ? (int)blocks[i + 1].Bounds.Top : _region.Height;
                int clearance = (int)(block.AverageLineHeight * 0.5f);
                int available = Math.Max(rect.Height, Math.Min(nextTop - clearance - rect.Top, (int)(rect.Height * 1.25f)));

                // Start near the original text size and shrink until the translation fits.
                // OCR line boxes are a little shorter than the font size, and Arabic letters look
                // smaller than Latin ones at the same size, so start slightly larger.
                float px = Clamp(block.AverageLineHeight * 1.0f, MinFontPx, MaxFontPx);
                Font font = null;
                Size measured;
                int textWidth = Math.Max(20, rect.Width - 2 * PadX);
                while (true)
                {
                    font?.Dispose();
                    font = new Font(Config.OverlayFontFamily, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                    measured = TextRenderer.MeasureText(block.Translation, font, new Size(textWidth, int.MaxValue), flags);
                    if (measured.Height + 2 * PadY <= available || px <= MinFontPx) break;
                    px = Math.Max(MinFontPx, px - 1f);
                }

                int top = Math.Max(rect.Top, prevBottom + 2);
                int height = Math.Max(rect.Height, measured.Height + 2 * PadY);
                rect = new Rectangle(rect.X, top, rect.Width, height);
                prevBottom = rect.Bottom;
                maxBottom = Math.Max(maxBottom, rect.Bottom);

                _patches.Add(new Patch
                {
                    Block = block,
                    Rect = rect,
                    SourceRect = source,
                    TextRect = new Rectangle(rect.X + PadX, rect.Y + (rect.Height - measured.Height) / 2, textWidth, measured.Height),
                    Text = block.Translation,
                    Font = font,
                    Back = back,
                    Fore = fore,
                    Flags = flags,
                    Bold = bold,
                    Stroke = stroke
                });
            }

            // If the translation needs more room than the original, grow downwards (within the monitor).
            if (maxBottom > _region.Height)
            {
                var screen = Screen.FromRectangle(_region).Bounds;
                int height = Math.Min(maxBottom, screen.Bottom - _region.Top);
                Bounds = new Rectangle(_region.X, _region.Y, _region.Width, Math.Max(_region.Height, height));
            }
        }

        private static TextFormatFlags DirectionFlags =>
            Config.TargetIsRightToLeft ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;

        private static Rectangle ClampHorizontally(Rectangle r, int width)
        {
            int left = Math.Max(0, r.Left);
            int right = Math.Min(width, r.Right);
            if (right - left < 20) { left = 0; right = width; }
            return Rectangle.FromLTRB(left, r.Top, right, r.Bottom);
        }

        private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(TransparentKey);
            // The selected area stays frozen while the translation is shown.
            g.DrawImageUnscaled(_image, 0, 0);

            switch (_mode)
            {
                case Mode.Loading:
                    using (var pen = new Pen(Accent, 2f) { DashPattern = new[] { 4f, 3f } })
                        g.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
                    DrawPill(g, "جاري الترجمة…", topRight: true);
                    break;

                case Mode.Result:
                    foreach (var p in _patches)
                    {
                        if (p == _peekPatch) continue;
                        using (var brush = new SolidBrush(p.Back))
                            g.FillRectangle(brush, p.Rect);
                        TextRenderer.DrawText(g, p.Text, p.Font, p.TextRect, p.Fore, p.Flags);
                    }
                    if (_peekPatch != null) DrawOriginal(g, _peekPatch);
                    break;

                case Mode.Message:
                    DrawPill(g, _message, topRight: false);
                    break;
            }
            DrawCloseButton(g);
        }

        /// <summary>Shows the captured original pixels of one patch, with the hovered word underlined.</summary>
        private void DrawOriginal(Graphics g, Patch p)
        {
            using (var brush = new SolidBrush(p.Back))
                g.FillRectangle(brush, p.Rect);
            g.DrawImage(_image, p.SourceRect, p.SourceRect, GraphicsUnit.Pixel);

            var frame = Rectangle.Union(p.Rect, p.SourceRect);
            using (var pen = new Pen(Color.FromArgb(150, Accent), 1f))
                g.DrawRectangle(pen, frame.X, frame.Y, frame.Width - 1, frame.Height - 1);

            if (_hoverWord != null)
            {
                var r = Rectangle.Round(_hoverWord.Bounds);
                r.Inflate(2, 1);
                using (var fill = new SolidBrush(Color.FromArgb(55, Accent)))
                    g.FillRectangle(fill, r);
                int thickness = Math.Max(2, (int)Math.Round(2 * _scale));
                using (var line = new SolidBrush(Accent))
                    g.FillRectangle(line, r.Left, r.Bottom, r.Width, thickness);
            }
        }

        private void DrawPill(Graphics g, string text, bool topRight)
        {
            var flags = TextFormatFlags.RightToLeft | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak |
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            using (var font = new Font(Config.OverlayFontFamily, 14f * _scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int maxWidth = Math.Max(120, Math.Min((int)(460 * _scale), ClientSize.Width - 16));
                var size = TextRenderer.MeasureText(text, font, new Size(maxWidth, int.MaxValue), flags);
                int padX = (int)(14 * _scale), padY = (int)(8 * _scale);
                int w = size.Width + 2 * padX, h = size.Height + 2 * padY;

                int x = topRight ? CloseButtonRect.Left - w - 6 : (ClientSize.Width - w) / 2;
                int y = topRight ? 6 : Math.Max(4, (ClientSize.Height - h) / 2);
                var pill = new Rectangle(Math.Max(0, x), y, w, h);

                using (var bg = new SolidBrush(PillBack))
                    g.FillRectangle(bg, pill);
                using (var border = new Pen(Accent, 1f))
                    g.DrawRectangle(border, pill.X, pill.Y, pill.Width - 1, pill.Height - 1);

                var textRect = new Rectangle(pill.X + padX, pill.Y + padY, size.Width, size.Height);
                TextRenderer.DrawText(g, text, font, textRect, Color.White, flags);
            }
        }

        // ---------- learning mode ----------

        private Patch PatchAt(Point p)
        {
            for (int i = _patches.Count - 1; i >= 0; i--)
            {
                var patch = _patches[i];
                if (patch.Rect.Contains(p) || (patch == _peekPatch && patch.SourceRect.Contains(p))) return patch;
            }
            return null;
        }

        private static OcrWordBox WordAt(Patch patch, Point p)
        {
            foreach (var line in patch.Block.Lines)
                foreach (var word in line.Words)
                    if (RectangleF.Inflate(word.Bounds, 2, 2).Contains(p) && WordFlow.CleanWord(word.Text) != null)
                        return word;
            return null;
        }

        private bool CardOpen => _card != null && !_card.IsDisposed;

        private void SetPeek(Patch patch)
        {
            if (patch == _peekPatch) return;
            _peekPatch = patch;
            _hoverWord = null;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overClose = CloseButtonRect.Contains(e.Location);
            if (overClose != _closeHover)
            {
                _closeHover = overClose;
                Invalidate(Rectangle.Inflate(CloseButtonRect, 2, 2));
            }
            if (overClose)
            {
                Cursor = Cursors.Hand;
                return;
            }
            Cursor = Cursors.Default;
            if (_mode != Mode.Result) return;

            // Ignore the mouse until it actually moves, so the original doesn't flash up
            // just because the selection ended over a paragraph.
            if (!_mouseArmed)
            {
                var c = Cursor.Position;
                if (Math.Abs(c.X - _cursorAtShow.X) + Math.Abs(c.Y - _cursorAtShow.Y) < 8) return;
                _mouseArmed = true;
            }

            var patch = PatchAt(e.Location);
            if (patch != _hoverPatch)
            {
                _hoverPatch = patch;
                _peekTimer.Stop();
                if (patch != null && patch != _peekPatch) _peekTimer.Start();
                else if (patch == null && !CardOpen) SetPeek(null);
            }

            var word = _peekPatch != null ? WordAt(_peekPatch, e.Location) : null;
            if (word != _hoverWord)
            {
                _hoverWord = word;
                Invalidate();
            }
            Cursor = word != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_closeHover)
            {
                _closeHover = false;
                Invalidate(Rectangle.Inflate(CloseButtonRect, 2, 2));
            }
            _peekTimer.Stop();
            _hoverPatch = null;
            if (!CardOpen) SetPeek(null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (CloseButtonRect.Contains(e.Location))
            {
                Close();
                return;
            }
            if (_mode != Mode.Result) return;

            var patch = PatchAt(e.Location);
            if (patch == null) return;

            if (patch != _peekPatch)
            {
                _peekTimer.Stop();
                CloseCard();
                SetPeek(patch);
                return;
            }

            var word = WordAt(patch, e.Location);
            if (word == null)
            {
                CloseCard();
                return;
            }

            string lookup = WordFlow.CleanWord(word.Text);
            if (CardOpen && _card.Word == lookup) return;

            CloseCard();
            _card = new WordCard(lookup, patch.Block.Text, RectangleToScreen(Rectangle.Round(word.Bounds)));
            _card.FormClosed += (s, a) => { if (!IsDisposed && _hoverPatch == null) SetPeek(null); };
            _card.Show(this);
        }

        private void CloseCard()
        {
            if (CardOpen) _card.Close();
            _card = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (CardOpen) CloseCard();
                else Close();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyToClipboard(_translatedText);
            }
            base.OnKeyDown(e);
        }

        private static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch (ExternalException) { /* clipboard busy */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoClose?.Dispose();
                _peekTimer.Dispose();
                foreach (var p in _patches) p.Font.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------- colour sampling ----------

        /// <summary>Fast read-only copy of the captured pixels.</summary>
        private sealed class PixelData
        {
            private readonly int[] _argb;
            private readonly int _w, _h;

            public PixelData(Bitmap bmp)
            {
                _w = bmp.Width;
                _h = bmp.Height;
                _argb = new int[_w * _h];
                var data = bmp.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < _h; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), _argb, y * _w, _w);
                }
                finally { bmp.UnlockBits(data); }
            }

            private Color At(int x, int y) => Color.FromArgb(_argb[y * _w + x]);

            /// <summary>
            /// Background = the most common colour on a thin ring around the text.
            /// Text colour = the pixels inside that differ most from the background.
            /// </summary>
            public void SampleColors(Rectangle text, out Color back, out Color fore)
            {
                var ring = Rectangle.Inflate(text, 3, 3);
                ring.Intersect(new Rectangle(0, 0, _w, _h));
                if (ring.Width < 2 || ring.Height < 2)
                {
                    back = Color.White;
                    fore = Color.FromArgb(20, 20, 20);
                    return;
                }

                var buckets = new Dictionary<int, long[]>(); // key -> [count, r, g, b]
                void AddRing(int x, int y)
                {
                    var c = At(x, y);
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    if (!buckets.TryGetValue(key, out var acc)) buckets[key] = acc = new long[4];
                    acc[0]++; acc[1] += c.R; acc[2] += c.G; acc[3] += c.B;
                }
                for (int x = ring.Left; x < ring.Right; x++) { AddRing(x, ring.Top); AddRing(x, ring.Bottom - 1); }
                for (int y = ring.Top; y < ring.Bottom; y++) { AddRing(ring.Left, y); AddRing(ring.Right - 1, y); }

                var best = buckets.Values.OrderByDescending(a => a[0]).First();
                back = Color.FromArgb((int)(best[1] / best[0]), (int)(best[2] / best[0]), (int)(best[3] / best[0]));

                // Text colour.
                var inner = Rectangle.Intersect(text, new Rectangle(0, 0, _w, _h));
                int step = Math.Max(1, (int)Math.Sqrt(inner.Width * (double)inner.Height / 20000));
                int maxDist = 0;
                for (int y = inner.Top; y < inner.Bottom; y += step)
                    for (int x = inner.Left; x < inner.Right; x += step)
                        maxDist = Math.Max(maxDist, Distance(At(x, y), back));

                fore = Readable(back);
                if (maxDist >= 120)
                {
                    long n = 0, r = 0, gg = 0, b = 0;
                    int threshold = (int)(maxDist * 0.7);
                    for (int y = inner.Top; y < inner.Bottom; y += step)
                        for (int x = inner.Left; x < inner.Right; x += step)
                        {
                            var c = At(x, y);
                            if (Distance(c, back) < threshold) continue;
                            n++; r += c.R; gg += c.G; b += c.B;
                        }
                    if (n > 0)
                    {
                        var candidate = Color.FromArgb((int)(r / n), (int)(gg / n), (int)(b / n));
                        if (ContrastRatio(candidate, back) >= 3.0) fore = candidate;
                    }
                }
            }

            /// <summary>
            /// Typical stroke thickness of the text relative to its line height: the median length
            /// of horizontal runs of "ink" pixels. Bold text has noticeably thicker strokes.
            /// </summary>
            public double StrokeRatio(Rectangle text, Color back, float lineHeight)
            {
                var inner = Rectangle.Intersect(text, new Rectangle(0, 0, _w, _h));
                if (inner.Width < 4 || inner.Height < 4 || lineHeight < 4) return 0;

                int maxDist = 0;
                for (int y = inner.Top; y < inner.Bottom; y++)
                    for (int x = inner.Left; x < inner.Right; x++)
                        maxDist = Math.Max(maxDist, Distance(At(x, y), back));
                if (maxDist < 120) return 0;

                int threshold = maxDist / 2;
                var runs = new List<int>();
                for (int y = inner.Top; y < inner.Bottom; y++)
                {
                    int run = 0;
                    for (int x = inner.Left; x < inner.Right; x++)
                    {
                        if (Distance(At(x, y), back) >= threshold) run++;
                        else if (run > 0) { runs.Add(run); run = 0; }
                    }
                    if (run > 0) runs.Add(run);
                }
                if (runs.Count == 0) return 0;
                runs.Sort();
                return runs[runs.Count / 2] / (double)lineHeight;
            }

            private static int Distance(Color a, Color b) =>
                Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

            private static Color Readable(Color back) =>
                Luminance(back) > 0.45 ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 245, 245);

            private static double Luminance(Color c)
            {
                double Channel(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
                return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
            }

            private static double ContrastRatio(Color a, Color b)
            {
                double la = Luminance(a), lb = Luminance(b);
                return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
            }
        }
    }
}
