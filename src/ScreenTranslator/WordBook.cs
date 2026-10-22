using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ScreenTranslator
{
    internal sealed class SavedWord
    {
        public string Word;
        public string Meaning;
        public string Context;
        public DateTime Added;
    }

    /// <summary>
    /// The user's saved vocabulary, kept in %AppData%\ScreenTranslator\words.tsv
    /// (opens in Excel or Notepad).
    /// </summary>
    internal static class WordBook
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenTranslator", "words.tsv");

        public static event Action Changed;

        public static List<SavedWord> Load()
        {
            var words = new List<SavedWord>();
            if (!File.Exists(FilePath)) return words;

            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8).Skip(1))
            {
                var cols = line.Split('\t');
                if (cols.Length < 4 || cols[0].Length == 0) continue;
                DateTime.TryParse(cols[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var added);
                words.Add(new SavedWord { Word = cols[0], Meaning = cols[1], Context = cols[2], Added = added });
            }
            return words;
        }

        public static bool Contains(string word) =>
            Load().Any(w => string.Equals(w.Word, word, StringComparison.OrdinalIgnoreCase));

        public static void Add(SavedWord word)
        {
            var words = Load();
            words.RemoveAll(w => string.Equals(w.Word, word.Word, StringComparison.OrdinalIgnoreCase));
            words.Insert(0, word);
            Save(words);
        }

        public static void Remove(string word)
        {
            var words = Load();
            words.RemoveAll(w => string.Equals(w.Word, word, StringComparison.OrdinalIgnoreCase));
            Save(words);
        }

        private static void Save(List<SavedWord> words)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var sb = new StringBuilder();
            sb.AppendLine("Word\tMeaning\tContext\tAdded");
            foreach (var w in words)
            {
                sb.Append(Clean(w.Word)).Append('\t')
                  .Append(Clean(w.Meaning)).Append('\t')
                  .Append(Clean(w.Context)).Append('\t')
                  .AppendLine(w.Added.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            Changed?.Invoke();
        }

        private static string Clean(string s) =>
            (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
