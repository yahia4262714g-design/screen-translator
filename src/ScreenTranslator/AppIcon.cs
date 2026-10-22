using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator
{
    internal static class AppIcon
    {
        /// <summary>The icon embedded in the exe (app.ico), reused for the tray.</summary>
        public static Icon Load()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return new Icon(icon, SystemInformation.SmallIconSize);
            }
            catch
            {
                // fall through
            }
            return SystemIcons.Application;
        }
    }
}
