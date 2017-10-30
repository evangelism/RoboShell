using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoboShell
{
    public static class Config
    {
        public static string EmotionAPIKey = "d8fb34d74fea4c3ab0db3829b0a4fd96"; // "<Your Key Here>";
        public static string EmotionAPIEndpoint = "https://westus.api.cognitive.microsoft.com/emotion/v1.0";
        public static string FaceAPIKey = "e408f9b6c8e34aee8f5567dbea67df30";
        public static string FaceAPIEndpoint = "https://westeurope.api.cognitive.microsoft.com/face/v1.0";
        public static bool RecognizeEmotions = true;
        public static int MinBoringSeconds = 10;
        public static int MaxBoringSeconds = 11;
    }
}
