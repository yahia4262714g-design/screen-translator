using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Lightly dims the live screen while the user drags a rectangle. The screen is not frozen:
    /// only the chosen area is captured afterwards. The window never takes focus, so a
    /// full-screen game or app stays in front instead of minimising.
    /// </summary>
    internal sealed class SelectionForm : Form
    {
        private static readonly Color HoleKey = Color.FromArgb(255, 0, 255);
        private const int MinSize = 6;

        private readonly Rectangle _virtualBounds;
        private readonly FrameForm _frame = new FrameForm();
        private Point _start;
        private Rectangle _selection;
        private bool _dragging;
        private bool _finished;

        /// <summary>Raised with the chosen area in screen coordinates.</summary>
        public event Action<Rectangle> Completed;

        public event Action Cancelled;

        public SelectionForm(Rectangle virtualBounds)
        {
            _virtualBounds = virtualBounds;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(8, 10, 16);
            Opacity = 0.35;
            TransparencyKey = HoleKey; // the selected area is cut out, so it shows at full brightness
            Bounds = virtualBounds;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _virtualBounds;
        }

        public void Cancel()
        {
            if (_finished) return;
            _finished = true;
            Cancelled?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                using (var hole = new SolidBrush(HoleKey))
                    e.Graphics.FillRectangle(hole, _selection);
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
            Capture = true;
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
            Capture = false;

            if (_selection.Width < MinSize || _selection.Height < MinSize)
            {
                UpdateSelection(Rectangle.Empty);
                return;
            }

            if (_finished) return;
            _finished = true;
            var onScreen = new Rectangle(
                _selection.X + _virtualBounds.X, _selection.Y + _virtualBounds.Y,
                _selection.Width, _selection.Height);
            Completed?.Invoke(onScreen);
        }

        private void UpdateSelection(Rectangle next)
        {
            var dirty = Rectangle.Union(_selection, next);
            dirty.Inflate(2, 2);
            _selection = next;
            Invalidate(dirty);
            Update();

            if (next.Width > 0 && next.Height > 0)
            {
                var onScreen = next;
                onScreen.Offset(_virtualBounds.Location);
                _frame.ShowAround(onScreen);
            }
            else
            {
                _frame.Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _frame.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Crisp, fully opaque blue outline around the selection (click-through).</summary>
        private sealed class FrameForm : Form
        {
            private const int Thickness = 2;

            public FrameForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Theme.Accent;
                TransparencyKey = HoleKey;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TRANSPARENT = 0x20;
                    var cp = base.CreateParams;
                    cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation => true;

            public void ShowAround(Rectangle selection)
            {
                Bounds = Rectangle.Inflate(selection, Thickness, Thickness);
                if (!Visible) Show();
                Invalidate();
                Update();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Accent);
                using (var hole = new SolidBrush(HoleKey))
                    e.Graphics.FillRectangle(hole, Thickness, Thickness, Width - 2 * Thickness, Height - 2 * Thickness);
            }
        }
    }
}
