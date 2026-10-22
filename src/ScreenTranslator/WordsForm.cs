using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ScreenTranslator
{
    /// <summary>"كلماتي المحفوظة": the saved vocabulary with listen / delete / copy.</summary>
    internal sealed class WordsForm : Form
    {
        private readonly ListView _list;
        private readonly Label _empty;

        public WordsForm()
        {
            float s = Theme.ScaleAt(Cursor.Position);
            int P(float v) => (int)Math.Round(v * s);

            Text = "كلماتي المحفوظة — " + Config.AppNameArabic;
            Icon = AppIcon.Load();
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font(Theme.TextFont, 10f);
            Size = new Size(P(820), P(520));
            MinimumSize = new Size(P(480), P(300));

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                BorderStyle = BorderStyle.None,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true
            };
            _list.Columns.Add("الكلمة", P(150));
            _list.Columns.Add("المعنى", P(260));
            _list.Columns.Add("الجملة اللي جات فيها", P(270));
            _list.Columns.Add("التاريخ", P(110));
            _list.DoubleClick += (s2, e) => SpeakSelected();
            _list.KeyDown += (s2, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteSelected();
                else if (e.KeyCode == Keys.Enter) SpeakSelected();
            };

            _empty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Text = "لسا ما حفظت ولا كلمة.\nاضغط على أي كلمة إنجليزية بالترجمة، وبعدين ☆ عشان تحفظها هون."
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(P(8)),
                FlowDirection = FlowDirection.LeftToRight
            };
            buttons.Controls.Add(MakeButton("🔊  استمع", (s2, e) => SpeakSelected()));
            buttons.Controls.Add(MakeButton("حذف", (s2, e) => DeleteSelected()));
            buttons.Controls.Add(MakeButton("نسخ الكل", (s2, e) => CopyAll()));
            buttons.Controls.Add(MakeButton("فتح الملف", (s2, e) => OpenFile()));

            Controls.Add(_list);
            Controls.Add(_empty);
            Controls.Add(buttons);

            WordBook.Changed += OnWordsChanged;
            Reload();
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 3, 10, 3), UseVisualStyleBackColor = true };
            b.Click += onClick;
            return b;
        }

        private void OnWordsChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(Reload));
            else Reload();
        }

        private void Reload()
        {
            var words = WordBook.Load();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var w in words)
            {
                var item = new ListViewItem(w.Word) { Tag = w };
                item.SubItems.Add(w.Meaning);
                item.SubItems.Add(w.Context);
                item.SubItems.Add(w.Added == DateTime.MinValue ? "" : w.Added.ToString("yyyy/MM/dd"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _list.Visible = words.Count > 0;
            _empty.Visible = words.Count == 0;
        }

        private void SpeakSelected()
        {
            if (_list.SelectedItems.Count > 0) Speech.Speak(_list.SelectedItems[0].Text);
        }

        private void DeleteSelected()
        {
            foreach (var word in _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList())
                WordBook.Remove(word);
        }

        private void CopyAll()
        {
            var sb = new StringBuilder();
            foreach (var w in WordBook.Load()) sb.AppendLine(w.Word + " — " + w.Meaning);
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); } catch (System.Runtime.InteropServices.ExternalException) { }
        }

        private static void OpenFile()
        {
            if (!System.IO.File.Exists(WordBook.FilePath)) return;
            try { Process.Start(new ProcessStartInfo(WordBook.FilePath) { UseShellExecute = true }); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) WordBook.Changed -= OnWordsChanged;
            base.Dispose(disposing);
        }
    }
}
