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

        // Ctrl + Alt + T: select an area of the screen and translate it in place.
        public static readonly Hotkey RegionHotkey = new Hotkey(ctrl: true, alt: true, shift: false, win: false, key: Keys.T);

        // Ctrl + Alt + C: translate the text you have highlighted (no screenshot).
        public static readonly Hotkey SelectionHotkey = new Hotkey(ctrl: true, alt: true, shift: false, win: false, key: Keys.C);

        // Longest highlighted text sent for translation.
        public const int MaxSelectionLength = 4500;

        public const string AppName = "Screen Translator";
        public const string AppNameArabic = "مترجم الشاشة";

        // Font used for the translated text drawn over the screen.
        public const string OverlayFontFamily = "Segoe UI";
    }
}
