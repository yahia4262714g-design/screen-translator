using System;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Invisible message-only window that owns the global shortcut.
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x5354; // "ST"
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private bool _registered;

        public event EventHandler Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        public bool Register()
        {
            uint modifiers = NativeMethods.MOD_NOREPEAT;
            if (Config.HotkeyCtrl) modifiers |= NativeMethods.MOD_CONTROL;
            if (Config.HotkeyAlt) modifiers |= NativeMethods.MOD_ALT;
            if (Config.HotkeyShift) modifiers |= NativeMethods.MOD_SHIFT;
            if (Config.HotkeyWin) modifiers |= NativeMethods.MOD_WIN;

            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, (uint)Config.HotkeyKey);
            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
            DestroyHandle();
        }
    }
}
