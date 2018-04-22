using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace RoboLogic
{
//    public interface ISpeaker
//    {
//        void Speak(string s);
//        void ShutUp();
//        void Play(Uri filename);
//
//        bool CanPlay();
//    }

    public class UWPLocalSpeaker //: ISpeaker
    {
        public SpeechSynthesizer Synthesizer = new SpeechSynthesizer();
        public MediaElement Media { get; set; }

        private bool isPlaying = false;

        public UWPLocalSpeaker(MediaElement Media, VoiceGender G)
        {
            this.Media = Media;
            this.Media.AutoPlay = true;
            var v = (from x in SpeechSynthesizer.AllVoices
                     where (x.Gender == G && x.Language == "ru-RU")
                     select x).FirstOrDefault();
            if (v != null) Synthesizer.Voice = v;
        }

        public async Task Speak(string s) {
            var x = await Synthesizer.SynthesizeTextToStreamAsync(s);
            Media.SetSource(x, x.ContentType);
            Media.AutoPlay = true;
            Media.Play();
        }

        public async Task Speak(SpeechSynthesisStream s)
        {
            Media.SetSource(s, s.ContentType);
            Media.AutoPlay = true;
            Media.Play();
        }

        public void ShutUp() {
            Media.Stop();
        }

        public void Play(Uri audioUri)
        {
            Media.Source = audioUri;
            Media.AutoPlay = true;
            Media.Play();
        }

        public async Task Play(Uri audioUri, int duration) {

            Media.Source = audioUri;
            Media.AutoPlay = true;
            Media.Play();
            await Task.Delay(TimeSpan.FromMilliseconds(duration));
            Media.Stop();
        }

        public bool CanPlay() {
            return !isPlaying ;

        }
    }
}
