using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Small dictionary card for one English word: pronunciation, meanings, 🔊 and ☆ save.
    /// Never takes focus, so the window that opened it stays open.
    /// </summary>
    internal sealed class WordCard : PopupBase
    {
        private const TextFormatFlags Rtl = TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.WordBreak |
                                            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
        private const TextFormatFlags Ltr = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                                            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        private sealed class TextOp
        {
            public string Text;
            public Rectangle Rect;
            public Font Font;
            public Color Color;
            public TextFormatFlags Flags;
        }

        private readonly string _word;
        private readonly string _context;
        private readonly Rectangle _anchor;
        private readonly List<TextOp> _ops = new List<TextOp>();
        private readonly Font _wordFont, _phoneticFont, _mainFont, _labelFont, _bodyFont;
        private readonly IconButton _speak, _star;
        private WordInfo _info;
        private string _error;
        private int _dividerY;

        public string Word => _word;

        public WordCard(string word, string context, Rectangle anchorOnScreen, bool speak = true)
        {
            _word = word;
            _context = context;
            _anchor = anchorOnScreen;
            S = Theme.ScaleAt(anchorOnScreen.Location);

            _wordFont = Px(20f, FontStyle.Bold);
            _phoneticFont = Px(13f);
            _mainFont = Px(19f, FontStyle.Bold);
            _labelFont = Px(12f, FontStyle.Bold);
            _bodyFont = Px(14f);

            _speak = new IconButton { Glyph = Theme.IconSpeaker, Click = () => Speech.Speak(_word) };
            _star = new IconButton { Click = ToggleSaved };
            Buttons.Add(_speak);
            Buttons.Add(_star);
            UpdateStar();

            Relayout();
            LoadMeaning();
            if (speak) Speech.Speak(_word);
        }

        protected override bool NoActivate => true;

        private async void LoadMeaning()
        {
            try
            {
                _info = await Translator.LookupWordAsync(_word);
            }
            catch
            {
                _error = "ما قدرت أجيب معنى الكلمة. تأكد من الإنترنت.";
            }
            if (IsDisposed) return;
            Relayout();
            Invalidate();
        }

        private void Relayout()
        {
            _ops.Clear();
            int width = P(330);
            int pad = P(16);
            int inner = width - 2 * pad;
            int bs = P(32);

            // Header: word + pronunciation on the left, buttons on the right.
            _star.Rect = new Rectangle(width - pad - bs + P(6), pad - P(4), bs, bs);
            _speak.Rect = new Rectangle(_star.Rect.Left - bs - P(2), _star.Rect.Top, bs, bs);
            int textWidth = _speak.Rect.Left - pad - P(6);

            int y = pad - P(2);
            int wordH = TextRenderer.MeasureText(_word, _wordFont, Size.Empty, Ltr).Height;
            _ops.Add(new TextOp { Text = _word, Rect = new Rectangle(pad, y, textWidth, wordH), Font = _wordFont, Color = Theme.Text, Flags = Ltr });
            y += wordH;

            string phonetic = _info?.Phonetic;
            if (!string.IsNullOrEmpty(phonetic))
            {
                string shown = "/" + phonetic + "/";
                int h = TextRenderer.MeasureText(shown, _phoneticFont, Size.Empty, Ltr).Height;
                _ops.Add(new TextOp { Text = shown, Rect = new Rectangle(pad, y, textWidth, h), Font = _phoneticFont, Color = Theme.Dim, Flags = Ltr });
                y += h;
            }

            y = Math.Max(y, _star.Rect.Bottom) + P(10);
            _dividerY = y;
            y += P(12);

            if (_info == null)
            {
                y = AddRtl(_error ?? "جاري البحث…", _bodyFont, _error != null ? Theme.Dim : Theme.Faint, pad, y, inner) + P(4);
            }
            else
            {
                if (!string.IsNullOrEmpty(_info.Translation))
                    y = AddRtl(_info.Translation, _mainFont, Theme.Text, pad, y, inner) + P(10);

                foreach (var meaning in _info.Meanings.Take(4))
                {
                    y = AddRtl(meaning.PartOfSpeech, _labelFont, Theme.Accent, pad, y, inner) + P(2);
                    y = AddRtl(string.Join("، ", meaning.Terms), _bodyFont, Theme.Dim, pad, y, inner) + P(10);
                }
            }

            PlaceNear(_anchor, new Size(width, y + pad - P(6)));
        }

        private int AddRtl(string text, Font font, Color color, int x, int y, int width)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Rtl);
            _ops.Add(new TextOp { Text = text, Rect = new Rectangle(x, y, width, size.Height), Font = font, Color = color, Flags = Rtl });
            return y + size.Height;
        }

        private void ToggleSaved()
        {
            if (WordBook.Contains(_word))
            {
                WordBook.Remove(_word);
            }
            else
            {
                string meaning = _info?.Translation ?? "";
                var extra = _info?.Meanings.SelectMany(m => m.Terms).Where(t => t != meaning).Take(3).ToList();
                if (extra != null && extra.Count > 0) meaning += " (" + string.Join("، ", extra) + ")";
                WordBook.Add(new SavedWord { Word = _word, Meaning = meaning, Context = _context, Added = DateTime.Now });
            }
            UpdateStar();
            Invalidate();
        }

        private void UpdateStar()
        {
            bool saved = WordBook.Contains(_word);
            _star.Glyph = saved ? Theme.IconStarFilled : Theme.IconStar;
            _star.Color = saved ? Theme.Star : Theme.Dim;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Back);
            foreach (var op in _ops)
                TextRenderer.DrawText(g, op.Text, op.Font, op.Rect, op.Color, op.Flags);

            using (var pen = new Pen(Theme.Border, Math.Max(1f, S)))
                g.DrawLine(pen, P(16), _dividerY, Width - P(16), _dividerY);

            DrawButtons(g);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _wordFont.Dispose();
                _phoneticFont.Dispose();
                _mainFont.Dispose();
                _labelFont.Dispose();
                _bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
