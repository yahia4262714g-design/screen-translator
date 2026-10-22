using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace ScreenTranslator
{
    /// <summary>Colours, icon glyphs and DPI helpers shared by the popups.</summary>
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(31, 32, 38);
        public static readonly Color Hover = Color.FromArgb(48, 50, 59);
        public static readonly Color Border = Color.FromArgb(62, 65, 76);
        public static readonly Color Text = Color.FromArgb(242, 243, 245);
        public static readonly Color Dim = Color.FromArgb(160, 166, 180);
        public static readonly Color Faint = Color.FromArgb(112, 117, 130);
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color Star = Color.FromArgb(255, 194, 75);

        public const string TextFont = "Segoe UI";

        // Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10) code points.
        public const string IconSpeaker = "";
        public const string IconCopy = "";
        public const string IconStar = "";
        public const string IconStarFilled = "";
        public const string IconCheck = "";

        private static string _iconFont;

        public static string IconFont
        {
            get
            {
                if (_iconFont != null) return _iconFont;
                using (var fonts = new InstalledFontCollection())
                {
                    var names = fonts.Families.Select(f => f.Name).ToList();
                    _iconFont = names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                }
                return _iconFont;
            }
        }

        /// <summary>Scale factor (1.0 = 100%) of the monitor containing <paramref name="screenPoint"/>.</summary>
        public static float ScaleAt(Point screenPoint)
        {
            try
            {
                const uint MONITOR_DEFAULTTONEAREST = 2;
                var monitor = NativeMethods.MonitorFromPoint(
                    new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y }, MONITOR_DEFAULTTONEAREST);
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0)
                    return dpi / 96f;
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            return 1f;
        }
    }
}
