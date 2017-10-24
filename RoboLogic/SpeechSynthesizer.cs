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
    }

    public class UWPLocalSpeaker : ISpeaker
    {
        public SpeechSynthesizer Synthesizer = new SpeechSynthesizer();
        public MediaElement Media { get; set; }

        public UWPLocalSpeaker(MediaElement Media)
        {
            this.Media = Media;
        }

        public async void Speak(string s)
        {
            var x = await Synthesizer.SynthesizeTextToStreamAsync(s);
            Media.AutoPlay = true;
            Media.SetSource(x, x.ContentType);
            Media.Play();
        }
    }
}
