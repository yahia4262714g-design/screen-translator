using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Full-screen frozen snapshot of every monitor where the user drags a rectangle,
    /// like the Windows Snipping Tool.
    /// </summary>
    internal sealed class SelectionForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(79, 140, 255);
        private const int MinSize = 6;

        private readonly Bitmap _screen;
        private readonly Bitmap _dimmed;
        private readonly Rectangle _virtualBounds;

        private Point _start;
        private Rectangle _selection;
        private bool _dragging;

        /// <summary>The chosen area in screen coordinates.</summary>
        public Rectangle SelectedScreenRect { get; private set; }

        /// <summary>The pixels inside the chosen area (caller owns it).</summary>
        public Bitmap SelectedImage { get; private set; }

        public SelectionForm(Bitmap screen, Rectangle virtualBounds)
        {
            _screen = screen;
            _virtualBounds = virtualBounds;
            _dimmed = BuildDimmedBackground(screen, virtualBounds);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Bounds = virtualBounds;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _virtualBounds;
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

            if (_selection.Width <= 0 || _selection.Height <= 0) return;

            g.DrawImage(_screen, _selection, _selection, GraphicsUnit.Pixel);

            using (var pen = new Pen(Accent, 2f) { Alignment = PenAlignment.Outset })
                g.DrawRectangle(pen, _selection);

            DrawSizeLabel(g);
        }

        private void DrawSizeLabel(Graphics g)
        {
            string label = _selection.Width + " × " + _selection.Height;
            using (var font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var size = TextRenderer.MeasureText(label, font);
                int x = _selection.Left;
                int y = _selection.Top - size.Height - 6;
                if (y < 0) y = _selection.Top + 4;
                var box = new Rectangle(x, y, size.Width + 8, size.Height + 2);
                using (var bg = new SolidBrush(Color.FromArgb(220, 20, 22, 28)))
                    g.FillRectangle(bg, box);
                TextRenderer.DrawText(g, label, font, box, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Cancel();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _dragging = true;
            _start = e.Location;
            UpdateSelection(new Rectangle(e.Location, Size.Empty));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            int x = Math.Min(_start.X, e.X), y = Math.Min(_start.Y, e.Y);
            int w = Math.Abs(e.X - _start.X), h = Math.Abs(e.Y - _start.Y);
            UpdateSelection(Rectangle.Intersect(new Rectangle(x, y, w, h), ClientRectangle));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;

            if (_selection.Width < MinSize || _selection.Height < MinSize)
            {
                UpdateSelection(Rectangle.Empty);
                return;
            }

            SelectedImage = _screen.Clone(_selection, PixelFormat.Format32bppArgb);
            SelectedScreenRect = new Rectangle(
                _selection.X + _virtualBounds.X, _selection.Y + _virtualBounds.Y,
                _selection.Width, _selection.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Cancel();
            base.OnKeyDown(e);
        }

        private void Cancel()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void UpdateSelection(Rectangle next)
        {
            var dirty = Rectangle.Union(_selection, next);
            dirty.Inflate(4, 40);
            _selection = next;
            Invalidate(dirty);
        }

        private static Bitmap BuildDimmedBackground(Bitmap screen, Rectangle virtualBounds)
        {
            var dimmed = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dimmed))
            {
                g.DrawImageUnscaled(screen, 0, 0);
                using (var shade = new SolidBrush(Color.FromArgb(120, 8, 10, 16)))
                    g.FillRectangle(shade, 0, 0, screen.Width, screen.Height);

                // Hint pill at the top of the primary monitor.
                var primary = Screen.PrimaryScreen.Bounds;
                primary.Offset(-virtualBounds.X, -virtualBounds.Y);
                string hint = "حدّد المنطقة اللي بدك تترجمها   •   Esc للإلغاء";
                // GDI+ text (not TextRenderer) because GDI would punch holes in a 32bpp ARGB bitmap's alpha.
                using (var font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var format = new StringFormat(StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var size = Size.Ceiling(g.MeasureString(hint, font, PointF.Empty, format));
                    var pill = new Rectangle(
                        primary.Left + (primary.Width - size.Width - 40) / 2, primary.Top + 28,
                        size.Width + 40, size.Height + 18);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = RoundedRect(pill, pill.Height / 2))
                    using (var bg = new SolidBrush(Color.FromArgb(235, 22, 24, 30)))
                    using (var border = new Pen(Color.FromArgb(90, Accent), 1f))
                    {
                        g.FillPath(bg, path);
                        g.DrawPath(border, path);
                    }
                    g.DrawString(hint, font, Brushes.White, pill, format);
                }
            }
            return dimmed;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) _dimmed.Dispose();
            base.Dispose(disposing);
        }
    }
}
