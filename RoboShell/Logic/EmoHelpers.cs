using Microsoft.ProjectOxford.Common.Contract;
using Microsoft.ProjectOxford.Emotion.Contract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RoboShell.Logic
{
    public static class EmoHelpers
    {
        public static EmotionScores AvEmotions(this IEnumerable<EmotionScores> S)
        {
            if (S.Count() == 0) return null;
            if (S.Count() == 1) return S.First();

            var E = new EmotionScores();
            foreach(var x in E.GetType().GetProperties())
            {
                float f = 0; int c = 0;
                foreach(var z in S)
                {
                    c++; f += (float)x.GetValue(z);
                }
                x.SetValue(E, f / c);
            }
            return E;
        }

        public static Tuple<string, float> MainEmotion(this EmotionScores s, bool UseNeutral = false)
        {
            float m = 0;
            string e = "";
            foreach (var p in s.GetType().GetProperties())
            {
                if (!UseNeutral && p.Name == "Neutral") continue;
                if ((float)p.GetValue(s) > m)
                {
                    m = (float)p.GetValue(s);
                    e = p.Name;
                }
            }
            return new Tuple<string, float>(e, m);
        }

    }
}
