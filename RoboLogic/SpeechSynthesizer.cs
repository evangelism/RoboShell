using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;

namespace RoboLogic
{
    public interface ISpeaker
    {
        void Speak(string s);
        void ShutUp();
        void Play(Uri filename);
    }

    public class UWPLocalSpeaker : ISpeaker
    {
        public SpeechSynthesizer Synthesizer = new SpeechSynthesizer();
        public MediaElement Media { get; set; }

        public UWPLocalSpeaker(MediaElement Media, VoiceGender G)
        {
            this.Media = Media;
            var v = (from x in SpeechSynthesizer.AllVoices
                     where (x.Gender == G && x.Language == "ru-RU")
                     select x).FirstOrDefault();
            if (v != null) Synthesizer.Voice = v;
        }

        public async void Speak(string s)
        {
            var x = await Synthesizer.SynthesizeTextToStreamAsync(s);
            Media.AutoPlay = true; // that's the default value
            Media.SetSource(x, x.ContentType);
            Media.Volume = 1.0;
            Media.Play();
        }

        public void ShutUp() {
            Media.Stop();
        }

        public void Play(Uri audioUri)
        {
            Media.Source = audioUri;
            Media.Play();
        }
    }
}
