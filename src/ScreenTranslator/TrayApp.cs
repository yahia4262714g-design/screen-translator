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
        private const int EscapeHotkeyId = 3;
        private static readonly Hotkey EscapeKey = new Hotkey(false, false, false, false, Keys.Escape);

        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkeys;
        private readonly ToolStripMenuItem _startupItem;
        private bool _selecting;
        private bool _copying;
        private OverlayForm _overlay;
        private TextPopup _popup;
        private WordsForm _wordsForm;
        private SelectionForm _selector;
        private MessageToast _hint;

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
                else if (id == EscapeHotkeyId) OnEscape();
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
            UpdateEscapeKey();
        }

        // ---------- Esc ----------
        // Our windows never take focus (so games stay in front), which means they never see the
        // keyboard. Esc is therefore claimed globally, but only while something of ours is open.

        private bool OverlayOpen => _overlay != null && !_overlay.IsDisposed;
        private bool PopupOpen => _popup != null && !_popup.IsDisposed;

        private void UpdateEscapeKey()
        {
            bool needed = _selector != null || OverlayOpen || PopupOpen;
            if (needed && !_hotkeys.IsRegistered(EscapeHotkeyId)) _hotkeys.Register(EscapeHotkeyId, EscapeKey);
            else if (!needed) _hotkeys.Unregister(EscapeHotkeyId);
        }

        private void OnEscape()
        {
            if (_selector != null) _selector.Cancel();
            else if (OverlayOpen) _overlay.HandleEscape();
            else if (PopupOpen) _popup.HandleEscape();
            UpdateEscapeKey();
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
            popup.FormClosed += (s, e) => { if (_popup == popup) _popup = null; UpdateEscapeKey(); };
            popup.Show();
            UpdateEscapeKey();

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
                if (OverlayOpen || PopupOpen) CloseOpenWindows();

                var chosen = await ChooseAreaAsync();
                if (chosen == null) return;
                region = chosen.Value;

                await Task.Delay(60); // let the selection layer disappear before capturing
                image = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(image))
                    g.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
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
                UpdateEscapeKey();
            };
            overlay.Show();
            UpdateEscapeKey();

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

        /// <summary>Lets the user drag a rectangle over the live screen; null when cancelled.</summary>
        private Task<Rectangle?> ChooseAreaAsync()
        {
            var done = new TaskCompletionSource<Rectangle?>();
            var selector = new SelectionForm(SystemInformation.VirtualScreen);
            selector.Completed += r => done.TrySetResult(r);
            selector.Cancelled += () => done.TrySetResult(null);
            _selector = selector;
            selector.Show();

            var primary = Screen.PrimaryScreen.Bounds;
            _hint = new MessageToast("حدّد المنطقة اللي بدك تترجمها  •  Esc أو كليك يمين للإلغاء", new Point(primary.Left, primary.Top + 20));
            _hint.Left = primary.Left + (primary.Width - _hint.Width) / 2;
            _hint.Show();
            UpdateEscapeKey();

            return done.Task.ContinueWith(t =>
            {
                _selector = null;
                selector.Close();
                if (_hint != null && !_hint.IsDisposed) _hint.Close();
                _hint = null;
                UpdateEscapeKey();
                return t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
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
