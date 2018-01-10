using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoboShellCognitiveLogic
{
    class Config {
        public static string CosmosDBKey = "zmUv9KoOuDmQ5DKIHaJolpOsni8A6Gr699NwlCeUPbDzpz67DZjXJGs6rMrqd7JUecnEs4SeptuuckWVJKmQlw==";
        public static string CosmosDBEndpoint = "https://roboshell-cosmosdb.documents.azure.com:443/";
        public static string CosmosDBDatabaseName = "log";
        public static string CosmosDBCollectionName = "facesCognitiveLog";
        public static string EmotionAPIKey = "67ffe30f42b14121acf1ea600967d4dc";
        public static string EmotionAPIEndpoint = "https://westus.api.cognitive.microsoft.com/emotion/v1.0/recognize";
        public static string FaceAPIKey = "e82d7914cb3c479583c6bb3ff10ca7c0";
        public static string FaceAPIEndpoint = "https://westeurope.api.cognitive.microsoft.com/face/v1.0/detect";
    }
}