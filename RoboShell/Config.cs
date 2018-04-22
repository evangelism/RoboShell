using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoboShell
{
    public static class Config
    {
        public static string CognitiveEndpoint = "https://roboshellcognitivelogic.azurewebsites.net/api/PhotosAnalyzer";
        public static bool RecognizeEmotions = true;
        public static int MinBoringSeconds = 10;
        public static int MaxBoringSeconds = 11;
        public static bool Headless = false;
        public static string KBFileName = "testFacePreDropout.brc";
        public static bool analyzeOnlyOneFace = true;
        public static int[] InputPinsNumbers = { 6, 13, 19, 26 };
        public static double facesRelation = 1.5;
        public static double biggestFaceRelativeSize = 0.01;
    }
}
