using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenTranslator
{
    /// <summary>
    /// Lives in the system tray, owns the global shortcuts and runs the translations.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ScreenTranslator";
        private const int RegionHotkeyId = 1;
        private const int SelectionHotkeyId = 2;

        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkeys;
        private readonly ToolStripMenuItem _startupItem;
        private bool _selecting;
        private bool _copying;
        private OverlayForm _overlay;
        private TextPopup _popup;
        private WordsForm _wordsForm;

        public TrayApp()
        {
            _startupItem = new ToolStripMenuItem("التشغيل مع ويندوز", null, (s, e) => ToggleStartup())
            {
                Checked = IsStartupEnabled()
            };

            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
            menu.Items.Add("ترجمة منطقة من الشاشة   (" + Config.RegionHotkey + ")", null, (s, e) => StartRegionTranslation());
            menu.Items.Add("ترجمة النص المحدد   (" + Config.SelectionHotkey + ")", null,
                (s, e) => new MessageToast("ظلّل أي نص إنجليزي واضغط " + Config.SelectionHotkey, Cursor.Position).Show());
            menu.Items.Add("كلماتي المحفوظة", null, (s, e) => ShowWords());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add("خروج", null, (s, e) => ExitThread());

            _tray = new NotifyIcon
            {
                Icon = AppIcon.Load(),
                Text = Config.AppNameArabic,
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartRegionTranslation(); };

            _hotkeys = new HotkeyWindow();
            _hotkeys.Pressed += id =>
            {
                if (id == RegionHotkeyId) StartRegionTranslation();
                else if (id == SelectionHotkeyId) StartSelectionTranslation();
            };

            bool regionOk = _hotkeys.Register(RegionHotkeyId, Config.RegionHotkey);
            bool selectionOk = _hotkeys.Register(SelectionHotkeyId, Config.SelectionHotkey);

            if (regionOk && selectionOk)
            {
                _tray.ShowBalloonTip(4000, Config.AppNameArabic + " شغّال",
                    Config.SelectionHotkey + "  ←  ترجمة النص المحدد\n" + Config.RegionHotkey + "  ←  ترجمة منطقة من الشاشة",
                    ToolTipIcon.None);
            }
            else
            {
                string busy = !regionOk && !selectionOk ? Config.RegionHotkey + " و " + Config.SelectionHotkey
                            : !regionOk ? Config.RegionHotkey.ToString() : Config.SelectionHotkey.ToString();
                _tray.ShowBalloonTip(6000, Config.AppNameArabic,
                    "الاختصار " + busy + " محجوز من برنامج ثاني. تقدر تستخدم أيقونة البرنامج جنب الساعة بداله.",
                    ToolTipIcon.Warning);
            }
        }

        private void CloseOpenWindows()
        {
            if (_overlay != null && !_overlay.IsDisposed) _overlay.Close();
            if (_popup != null && !_popup.IsDisposed) _popup.Close();
            _overlay = null;
            _popup = null;
        }

        // ---------- Ctrl+Alt+C: highlighted text ----------

        private async void StartSelectionTranslation()
        {
            if (_copying || _selecting) return;
            _copying = true;

            string text;
            Point anchor;
            try
            {
                CloseOpenWindows();
                text = await SelectedText.CopyAsync();
                anchor = Cursor.Position;
            }
            finally
            {
                _copying = false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                new MessageToast("ظلّل نص إنجليزي أول، وبعدين اضغط " + Config.SelectionHotkey +
                                 "\nأو استخدم " + Config.RegionHotkey + " لتحديد منطقة من الشاشة.", anchor).Show();
                return;
            }

            text = SelectedText.Normalize(text);
            var popup = new TextPopup(text, anchor);
            _popup = popup;
            popup.FormClosed += (s, e) => { if (_popup == popup) _popup = null; };
            popup.Show();

            try
            {
                popup.SetTranslation(await Translator.TranslateAsync(text));
            }
            catch (TranslationException ex)
            {
                popup.SetError(ex.Message);
            }
            catch (Exception ex)
            {
                popup.SetError("صار خطأ: " + ex.Message);
            }
        }

        // ---------- Ctrl+Alt+T: area of the screen ----------

        private async void StartRegionTranslation()
        {
            if (_selecting) return;
            _selecting = true;

            Rectangle region;
            Bitmap image;
            try
            {
                if ((_overlay != null && !_overlay.IsDisposed) || (_popup != null && !_popup.IsDisposed))
                {
                    CloseOpenWindows();
                    await Task.Delay(80); // let them vanish before the screenshot
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

        // ---------- tray menu ----------

        private void ShowWords()
        {
            if (_wordsForm == null || _wordsForm.IsDisposed)
            {
                _wordsForm = new WordsForm();
                _wordsForm.Show();
            }
            if (_wordsForm.WindowState == FormWindowState.Minimized) _wordsForm.WindowState = FormWindowState.Normal;
            _wordsForm.Activate();
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
            CloseOpenWindows();
            if (_wordsForm != null && !_wordsForm.IsDisposed) _wordsForm.Close();
            _hotkeys.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
