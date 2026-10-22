using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Dark, borderless, rounded (Windows 11) popup with a drop shadow and simple icon buttons.
    /// Everything is drawn by hand in real screen pixels, scaled for the monitor's DPI.
    /// </summary>
    internal abstract class PopupBase : Form
    {
        protected sealed class IconButton
        {
            public Rectangle Rect;
            public string Glyph;
            public Color Color = Theme.Dim;
            public Action Click;
        }

        protected readonly List<IconButton> Buttons = new List<IconButton>();
        private IconButton _hoverButton;

        /// <summary>DPI scale of the monitor the popup is on.</summary>
        protected float S = 1f;

        protected PopupBase()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Back;
        }

        /// <summary>When true the popup never takes focus from the window that opened it.</summary>
        protected virtual bool NoActivate => false;

        protected override bool ShowWithoutActivation => NoActivate;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                if (NoActivate) cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int border = ColorTranslator.ToWin32(Theme.Border);
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
        }

        protected int P(float v) => (int)Math.Round(v * S);

        protected Font Px(float px, FontStyle style = FontStyle.Regular, string family = Theme.TextFont) =>
            new Font(family, px * S, style, GraphicsUnit.Pixel);

        /// <summary>Puts the popup just below <paramref name="anchor"/> (or above it when there is no room).</summary>
        protected void PlaceNear(Rectangle anchor, Size size)
        {
            var area = Screen.FromPoint(anchor.Location).WorkingArea;
            int gap = P(8);
            int x = anchor.Left;
            int y = anchor.Bottom + gap;
            if (y + size.Height > area.Bottom) y = anchor.Top - size.Height - gap;
            x = Math.Max(area.Left + gap, Math.Min(x, area.Right - size.Width - gap));
            y = Math.Max(area.Top + gap, Math.Min(y, area.Bottom - size.Height - gap));
            Bounds = new Rectangle(x, y, size.Width, size.Height);
        }

        protected void DrawButtons(Graphics g)
        {
            using (var font = Px(15f, FontStyle.Regular, Theme.IconFont))
            {
                foreach (var b in Buttons)
                {
                    if (b == _hoverButton)
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var path = RoundedRect(b.Rect, P(6)))
                        using (var brush = new SolidBrush(Theme.Hover))
                            g.FillPath(brush, path);
                        g.SmoothingMode = SmoothingMode.None;
                    }
                    TextRenderer.DrawText(g, b.Glyph, font, b.Rect, b.Color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }
        }

        protected IconButton ButtonAt(Point p)
        {
            foreach (var b in Buttons) if (b.Rect.Contains(p)) return b;
            return null;
        }

        protected virtual Cursor CursorAt(Point p) => ButtonAt(p) != null ? Cursors.Hand : Cursors.Default;

        protected virtual void OnContentClick(Point p) { }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var b = ButtonAt(e.Location);
            if (b != _hoverButton)
            {
                if (_hoverButton != null) Invalidate(_hoverButton.Rect);
                if (b != null) Invalidate(b.Rect);
                _hoverButton = b;
            }
            Cursor = CursorAt(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverButton != null)
            {
                Invalidate(_hoverButton.Rect);
                _hoverButton = null;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var b = ButtonAt(e.Location);
            if (b != null) b.Click?.Invoke();
            else OnContentClick(e.Location);
        }

        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
