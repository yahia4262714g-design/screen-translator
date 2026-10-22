using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ScreenTranslator
{
    internal sealed class Hotkey
    {
        public readonly bool Ctrl, Alt, Shift, Win;
        public readonly Keys Key;

        public Hotkey(bool ctrl, bool alt, bool shift, bool win, Keys key)
        {
            Ctrl = ctrl; Alt = alt; Shift = shift; Win = win; Key = key;
        }

        public uint Modifiers
        {
            get
            {
                uint m = NativeMethods.MOD_NOREPEAT;
                if (Ctrl) m |= NativeMethods.MOD_CONTROL;
                if (Alt) m |= NativeMethods.MOD_ALT;
                if (Shift) m |= NativeMethods.MOD_SHIFT;
                if (Win) m |= NativeMethods.MOD_WIN;
                return m;
            }
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(Key.ToString());
            return string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// Invisible message-only window that owns the global shortcuts.
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private readonly List<int> _registered = new List<int>();

        /// <summary>Raised with the id passed to <see cref="Register"/>.</summary>
        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        public bool Register(int id, Hotkey hotkey)
        {
            bool ok = NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers, (uint)hotkey.Key);
            if (ok) _registered.Add(id);
            return ok;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Pressed?.Invoke(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in _registered) NativeMethods.UnregisterHotKey(Handle, id);
            _registered.Clear();
            DestroyHandle();
        }
    }
}
