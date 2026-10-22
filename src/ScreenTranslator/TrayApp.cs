using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenTranslator
{
    /// <summary>
    /// Lives in the system tray, owns the global shortcut and runs one translation at a time.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ScreenTranslator";

        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private readonly ToolStripMenuItem _startupItem;
        private bool _selecting;
        private OverlayForm _overlay;

        public TrayApp()
        {
            _startupItem = new ToolStripMenuItem("التشغيل مع ويندوز", null, (s, e) => ToggleStartup())
            {
                Checked = IsStartupEnabled()
            };

            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
            menu.Items.Add("ترجمة منطقة   (" + Config.HotkeyText + ")", null, (s, e) => StartTranslation());
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("خروج", null, (s, e) => ExitThread());

            _tray = new NotifyIcon
            {
                Icon = AppIcon.Load(),
                Text = Config.AppNameArabic + " — " + Config.HotkeyText,
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartTranslation(); };

            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += (s, e) => StartTranslation();

            if (_hotkey.Register())
            {
                _tray.ShowBalloonTip(3000, Config.AppNameArabic + " شغّال",
                    "اضغط " + Config.HotkeyText + " وحدّد أي نص على الشاشة.", ToolTipIcon.None);
            }
            else
            {
                _tray.ShowBalloonTip(6000, Config.AppNameArabic,
                    "الاختصار " + Config.HotkeyText + " محجوز من برنامج ثاني. تقدر تضغط على أيقونة البرنامج جنب الساعة بداله.",
                    ToolTipIcon.Warning);
            }
        }

        private async void StartTranslation()
        {
            if (_selecting) return;
            _selecting = true;

            Rectangle region;
            Bitmap image;
            try
            {
                if (_overlay != null && !_overlay.IsDisposed)
                {
                    _overlay.Close();
                    _overlay = null;
                    await System.Threading.Tasks.Task.Delay(80); // let it vanish before the screenshot
                }

                var bounds = SystemInformation.VirtualScreen;
                using (var screen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(screen))
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

                    using (var selector = new SelectionForm(screen, bounds))
                    {
                        if (selector.ShowDialog() != DialogResult.OK) return;
                        region = selector.SelectedScreenRect;
                        image = selector.SelectedImage;
                    }
                }
            }
            catch (Exception ex)
            {
                _tray.ShowBalloonTip(4000, Config.AppNameArabic, "ما قدرت آخذ صورة للشاشة: " + ex.Message, ToolTipIcon.Error);
                return;
            }
            finally
            {
                _selecting = false;
            }

            var overlay = new OverlayForm(region, image);
            _overlay = overlay;
            overlay.FormClosed += (s, e) =>
            {
                if (_overlay == overlay) _overlay = null;
                image.Dispose();
            };
            overlay.Show();

            try
            {
                var lines = await OcrService.RecognizeAsync(image);
                var blocks = TextLayout.GroupIntoBlocks(lines);
                if (blocks.Count == 0)
                {
                    overlay.ShowMessage("ما لقيت نص إنجليزي بهاي المنطقة", autoClose: true);
                    return;
                }

                await Translator.TranslateBlocksAsync(blocks);
                overlay.ShowTranslation(blocks);
            }
            catch (OcrUnavailableException ex)
            {
                overlay.ShowMessage(ex.Message, autoClose: false);
            }
            catch (TranslationException ex)
            {
                overlay.ShowMessage(ex.Message, autoClose: false);
            }
            catch (Exception ex)
            {
                overlay.ShowMessage("صار خطأ: " + ex.Message, autoClose: false);
            }
        }

        private static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key?.GetValue(RunValue) is string;
        }

        private void ToggleStartup()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (IsStartupEnabled()) key.DeleteValue(RunValue, false);
                else key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            }
            _startupItem.Checked = IsStartupEnabled();
        }

        protected override void ExitThreadCore()
        {
            _overlay?.Close();
            _hotkey.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
