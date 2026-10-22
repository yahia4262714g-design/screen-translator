using System;
using System.Linq;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace ScreenTranslator
{
    /// <summary>
    /// Speaks English text: Google's natural voice when online, the Windows voice otherwise.
    /// </summary>
    internal static class Speech
    {
        private const int GoogleMaxLength = 190;

        private static MediaPlayer _player;
        private static string _current;
        private static bool _usingWindowsVoice;

        public static void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _current = text.Trim();

            try
            {
                EnsurePlayer();
                if (_current.Length > GoogleMaxLength)
                {
                    SpeakWithWindows(_current);
                    return;
                }

                string url = "https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl="
                             + Config.SourceLanguage + "&q=" + Uri.EscapeDataString(_current);
                _usingWindowsVoice = false;
                _player.Source = MediaSource.CreateFromUri(new Uri(url));
                _player.Play();
            }
            catch
            {
                SpeakWithWindows(_current);
            }
        }

        private static void EnsurePlayer()
        {
            if (_player != null) return;
            _player = new MediaPlayer();
            _player.MediaFailed += (s, e) => { if (!_usingWindowsVoice) SpeakWithWindows(_current); };
        }

        private static async void SpeakWithWindows(string text)
        {
            try
            {
                EnsurePlayer();
                var synth = new SpeechSynthesizer();
                var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
                    v.Language.StartsWith(Config.SourceLanguage, StringComparison.OrdinalIgnoreCase));
                if (voice != null) synth.Voice = voice;

                var stream = await synth.SynthesizeTextToStreamAsync(text);
                _usingWindowsVoice = true;
                _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                _player.Play();
            }
            catch
            {
                // no voice available: stay silent rather than crash
            }
        }
    }
}
