using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.UI.Xaml.Controls;

namespace RoboLogic
{
    public interface ISpeaker
    {
        void Speak(string s);
        void ShutUp();
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
            Media.AutoPlay = true;
            Media.SetSource(x, x.ContentType);
            Media.Play();
        }

        public void ShutUp() {
            Media.Stop();
        }
    }
}
