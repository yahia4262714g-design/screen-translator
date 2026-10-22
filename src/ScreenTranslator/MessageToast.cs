using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>Short note shown next to the mouse that disappears on its own.</summary>
    internal sealed class MessageToast : PopupBase
    {
        private const TextFormatFlags Flags = TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.WordBreak |
                                              TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private readonly string _message;
        private readonly Font _font;
        private readonly Timer _timer;
        private Rectangle _textRect;

        public MessageToast(string message, Point anchorOnScreen)
        {
            _message = message;
            S = Theme.ScaleAt(anchorOnScreen);
            _font = Px(14f);

            int pad = P(14);
            var size = TextRenderer.MeasureText(message, _font, new Size(P(320), int.MaxValue), Flags);
            _textRect = new Rectangle(pad, pad, size.Width, size.Height);
            PlaceNear(new Rectangle(anchorOnScreen, new Size(1, P(12))), new Size(size.Width + 2 * pad, size.Height + 2 * pad));

            _timer = new Timer { Interval = 3000 };
            _timer.Tick += (s, e) => Close();
            _timer.Start();
        }

        protected override bool NoActivate => true;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Back);
            TextRenderer.DrawText(e.Graphics, _message, _font, _textRect, Theme.Text, Flags);
        }

        protected override void OnContentClick(Point p) => Close();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
