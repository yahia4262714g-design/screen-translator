using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Card shown next to the mouse for highlighted text (Ctrl+Alt+C): the Arabic translation,
    /// and below it the English original where every word can be clicked for its meaning.
    /// </summary>
    internal sealed class TextPopup : PopupBase
    {
        private const TextFormatFlags Rtl = TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.WordBreak |
                                            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl |
                                            TextFormatFlags.PreserveGraphicsClipping;

        private readonly string _original;
        private readonly Point _anchor;
        private readonly Font _translationFont, _originalFont, _hintFont;
        private readonly IconButton _speak, _copy;

        private string _translation;
        private string _error;
        private WordFlow _flow;
        private WordFlow.Item _hoverWord;
        private Rectangle _translationRect;
        private int _dividerY, _contentHeight, _viewHeight, _scroll;
        private WordCard _card;

        public TextPopup(string original, Point anchorOnScreen)
        {
            _original = original;
            _anchor = anchorOnScreen;
            S = Theme.ScaleAt(anchorOnScreen);

            _translationFont = Px(17f);
            _originalFont = Px(14.5f);
            _hintFont = Px(12f);

            _speak = new IconButton { Glyph = Theme.IconSpeaker, Click = () => Speech.Speak(_original) };
            _copy = new IconButton { Glyph = Theme.IconCopy, Click = CopyTranslation };
            Buttons.Add(_speak);
            Buttons.Add(_copy);

            Relayout();
        }

        public void SetTranslation(string translation)
        {
            if (IsDisposed) return;
            _translation = translation;
            Relayout();
            Invalidate();
        }

        public void SetError(string message)
        {
            if (IsDisposed) return;
            _error = message;
            Relayout();
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        // ---------- layout ----------

        private string TopText => _error ?? _translation ?? "جاري الترجمة…";

        private int FooterHeight => P(44);

        private void Relayout()
        {
            int pad = P(18);
            const TextFormatFlags single = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int natural = Math.Max(
                TextRenderer.MeasureText(TopText, _translationFont, Size.Empty, single).Width,
                TextRenderer.MeasureText(_original, _originalFont, Size.Empty, single).Width) + 2 * pad;
            int width = Math.Max(P(340), Math.Min(P(540), natural));
            int inner = width - 2 * pad;

            int y = pad;
            var size = TextRenderer.MeasureText(TopText, _translationFont, new Size(inner, int.MaxValue), Rtl);
            _translationRect = new Rectangle(pad, y, inner, size.Height);
            y += size.Height + P(14);

            _dividerY = y;
            y += P(14);

            _flow = WordFlow.Build(_original, _originalFont, pad, y, inner, P(5));
            y += _flow.Height + P(8);
            _contentHeight = y;

            var area = Screen.FromPoint(_anchor).WorkingArea;
            int maxView = (int)(area.Height * 0.7) - FooterHeight;
            _viewHeight = Math.Min(_contentHeight, Math.Max(P(120), maxView));
            _scroll = Math.Max(0, Math.Min(_scroll, _contentHeight - _viewHeight));

            int bs = P(32);
            int by = _viewHeight + (FooterHeight - bs) / 2;
            _speak.Rect = new Rectangle(pad - P(7), by, bs, bs);
            _copy.Rect = new Rectangle(_speak.Rect.Right + P(2), by, bs, bs);

            var anchorRect = new Rectangle(_anchor.X, _anchor.Y, 1, P(12));
            if (Visible)
            {
                // Grow in place once the translation arrives instead of jumping around.
                var bounds = Bounds;
                bounds.Size = new Size(width, _viewHeight + FooterHeight);
                if (bounds.Bottom > area.Bottom) bounds.Y = Math.Max(area.Top, area.Bottom - bounds.Height - P(8));
                if (bounds.Right > area.Right) bounds.X = Math.Max(area.Left, area.Right - bounds.Width - P(8));
                Bounds = bounds;
            }
            else
            {
                PlaceNear(anchorRect, new Size(width, _viewHeight + FooterHeight));
            }
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Back);

            var view = new Rectangle(0, 0, Width, _viewHeight);
            g.SetClip(view);

            var tr = _translationRect;
            tr.Offset(0, -_scroll);
            Color topColor = _error != null ? Theme.Dim : _translation == null ? Theme.Faint : Theme.Text;
            TextRenderer.DrawText(g, TopText, _translationFont, tr, topColor, Rtl);

            using (var pen = new Pen(Theme.Border, Math.Max(1f, S)))
                g.DrawLine(pen, P(18), _dividerY - _scroll, Width - P(18), _dividerY - _scroll);

            _flow.Draw(g, _originalFont, Theme.Dim, Theme.Accent, _hoverWord, -_scroll, Math.Max(1, P(1.5f)));

            g.ResetClip();

            // Footer.
            using (var pen = new Pen(Theme.Border, Math.Max(1f, S)))
                g.DrawLine(pen, 0, _viewHeight, Width, _viewHeight);

            var hintRect = new Rectangle(_copy.Rect.Right + P(8), _viewHeight, Width - _copy.Rect.Right - P(8) - P(18), FooterHeight);
            TextRenderer.DrawText(g, "اضغط على أي كلمة إنجليزية لتشوف معناها", _hintFont, hintRect, Theme.Faint,
                TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            DrawButtons(g);
        }

        // ---------- interaction ----------

        private WordFlow.Item WordAt(Point p) =>
            p.Y < _viewHeight ? _flow.HitTest(new Point(p.X, p.Y + _scroll)) : null;

        protected override Cursor CursorAt(Point p) =>
            ButtonAt(p) != null || WordAt(p) != null ? Cursors.Hand : Cursors.Default;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var word = WordAt(e.Location);
            if (word != _hoverWord)
            {
                _hoverWord = word;
                Invalidate(new Rectangle(0, 0, Width, _viewHeight));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverWord != null)
            {
                _hoverWord = null;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, _contentHeight - _viewHeight);
            if (max == 0) return;
            _scroll = Math.Max(0, Math.Min(max, _scroll - e.Delta / 120 * P(48)));
            CloseCard();
            Invalidate();
        }

        protected override void OnContentClick(Point p)
        {
            var word = WordAt(p);
            if (word == null)
            {
                CloseCard();
                return;
            }

            var r = word.Rect;
            r.Offset(0, -_scroll);
            var onScreen = RectangleToScreen(r);
            if (_card != null && !_card.IsDisposed && _card.Word == word.Lookup) return;

            CloseCard();
            _card = new WordCard(word.Lookup, _original, onScreen);
            _card.Show(this);
        }

        private void CloseCard()
        {
            if (_card != null && !_card.IsDisposed) _card.Close();
            _card = null;
        }

        private void CopyTranslation()
        {
            if (string.IsNullOrEmpty(_translation)) return;
            try { Clipboard.SetText(_translation); } catch (System.Runtime.InteropServices.ExternalException) { }
            _copy.Glyph = Theme.IconCheck;
            Invalidate(_copy.Rect);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (_card != null && !_card.IsDisposed) CloseCard();
                else Close();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyTranslation();
            }
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Clicking anywhere outside closes the card (the word card never takes focus).
            BeginInvoke(new Action(() => { if (!IsDisposed && Form.ActiveForm == null) Close(); }));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _translationFont.Dispose();
                _originalFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
