using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>
    /// Reads the text highlighted in whatever app is in front by sending it Ctrl+C,
    /// then puts the clipboard back the way it was.
    /// </summary>
    internal static class SelectedText
    {
        private static readonly string[] PreservedFormats =
        {
            DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html,
            DataFormats.Bitmap, DataFormats.FileDrop
        };

        public static async Task<string> CopyAsync()
        {
            // The shortcut keys are probably still held; Ctrl+Alt+C would not copy.
            for (int i = 0; i < 100 && AnyModifierDown(); i++)
                await Task.Delay(15);

            var backup = Backup();
            uint before = NativeMethods.GetClipboardSequenceNumber();
            SendCtrlC();

            string text = null;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(15);
                if (NativeMethods.GetClipboardSequenceNumber() == before) continue;

                await Task.Delay(30); // let the source app finish writing
                text = ReadText();
                Restore(backup);
                break;
            }
            return text;
        }

        /// <summary>
        /// Text copied from PDFs breaks every line; join lines that are clearly the same sentence.
        /// </summary>
        public static string Normalize(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (sb.Length == 0) { sb.Append(line); continue; }

                if (line.Length == 0)
                {
                    if (!sb.ToString().EndsWith("\n\n")) sb.Append("\n\n");
                    continue;
                }

                char last = sb[sb.Length - 1];
                bool continues = last != '\n' && ".!?:;\"”".IndexOf(last) < 0 && char.IsLower(line[0]);
                if (last == '\n') sb.Append(line);
                else if (continues && last == '-') { sb.Length--; sb.Append(line); }
                else sb.Append(continues ? " " : "\n").Append(line);
            }
            string result = Regex.Replace(sb.ToString(), "[ \t]{2,}", " ").Trim();
            if (result.Length > Config.MaxSelectionLength)
                result = result.Substring(0, Config.MaxSelectionLength) + "…";
            return result;
        }

        private static bool AnyModifierDown() =>
            NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL) || NativeMethods.IsKeyDown(NativeMethods.VK_MENU) ||
            NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT) || NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) ||
            NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);

        private static void SendCtrlC()
        {
            const ushort vkC = 0x43;
            var inputs = new[]
            {
                Key(NativeMethods.VK_CONTROL, false), Key(vkC, false),
                Key(vkC, true), Key(NativeMethods.VK_CONTROL, true)
            };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static NativeMethods.INPUT Key(int vk, bool up) => new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0 }
            }
        };

        private static string ReadText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch (ExternalException)
                {
                    System.Threading.Thread.Sleep(20); // clipboard briefly locked by another app
                }
            }
            return null;
        }

        private static Dictionary<string, object> Backup()
        {
            var saved = new Dictionary<string, object>();
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return saved;
                foreach (string format in PreservedFormats)
                {
                    try
                    {
                        if (!data.GetDataPresent(format, false)) continue;
                        var value = data.GetData(format, false);
                        if (value != null) saved[format] = value;
                    }
                    catch
                    {
                        // a format that cannot be read is simply not restored
                    }
                }
            }
            catch (ExternalException)
            {
            }
            return saved;
        }

        private static void Restore(Dictionary<string, object> saved)
        {
            try
            {
                if (saved.Count == 0)
                {
                    Clipboard.Clear();
                    return;
                }
                var data = new DataObject();
                foreach (var kv in saved) data.SetData(kv.Key, kv.Value);
                Clipboard.SetDataObject(data, true, 5, 50);
            }
            catch (ExternalException)
            {
            }
        }
    }
}
