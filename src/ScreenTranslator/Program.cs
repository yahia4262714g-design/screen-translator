using System;
using System.Threading;
using System.Windows.Forms;

namespace ScreenTranslator
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
                return SelfTest.Run(args.Length > 1 ? args[1] : "selftest.txt", args.Length > 2 ? args[2] : null);

            using (var mutex = new Mutex(true, @"Local\ScreenTranslator.SingleInstance", out bool firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show(
                        "مترجم الشاشة شغّال أصلاً — اضغط " + Config.SelectionHotkey + " أو " + Config.RegionHotkey + ".",
                        Config.AppNameArabic, MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
