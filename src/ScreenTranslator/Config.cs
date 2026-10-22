using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Everything you may want to change later lives here.
    /// </summary>
    internal static class Config
    {
        // Language read from the screen (OCR) and translated from.
        public const string SourceLanguage = "en";
        public const string OcrLanguageTag = "en-US";

        // Language the translation is shown in.
        public const string TargetLanguage = "ar";
        public const bool TargetIsRightToLeft = true;

        // Global shortcut: Ctrl + Alt + T
        public static readonly bool HotkeyCtrl = true;
        public static readonly bool HotkeyAlt = true;
        public static readonly bool HotkeyShift = false;
        public static readonly bool HotkeyWin = false;
        public const Keys HotkeyKey = Keys.T;
        public const string HotkeyText = "Ctrl + Alt + T";

        public const string AppName = "Screen Translator";
        public const string AppNameArabic = "مترجم الشاشة";

        // Font used for the translated text drawn over the screen.
        public const string OverlayFontFamily = "Segoe UI";
    }
}
