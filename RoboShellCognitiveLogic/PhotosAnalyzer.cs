using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Text;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;


namespace RoboShellCognitiveLogic {
    public static class PhotosAnalyzer {

        [FunctionName("PhotosAnalyzer")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post",
            Route = null)]HttpRequestMessage req, TraceWriter log) {
            log.Info("C# HTTP trigger function processed a request.");

            HttpResponseMessage response;
            try {

                PhotoToProcessDTO photoToProcessDTO = JsonConvert.DeserializeObject<PhotoToProcessDTO>(
                    await req.Content.ReadAsStringAsync());

                PhotoInfoDTO photoInfo = ProcessPhotoAsync(photoToProcessDTO.PhotoAsByteArray,
                    photoToProcessDTO.RecognizeEmotions, log);

                //await SaveToDatabase(photoInfo, log);

                var json = JsonConvert.SerializeObject(photoInfo);
                response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            catch (Exception e) {
                response = new HttpResponseMessage(HttpStatusCode.BadRequest) {
                    Content = new StringContent("{\"exception_message\": \"" + e.Message + "\"}",
                                                Encoding.UTF8, "application/json")
                };
            }

            return response;
        }


        public static PhotoInfoDTO ProcessPhotoAsync(byte[] photoAsByteArray, bool recognizeEmotions,
            TraceWriter log) {

            HttpClient client = new HttpClient();

            string emotion = "";
            FaceAPIInfoDTO faceAPIInfoDTO = new FaceAPIInfoDTO() {
                Age = "",
                FaceCountAsString = "",
                Gender = "",
                FoundAndProcessedFaces = false
            };

            Task[] tasks = new Task[2];
            tasks[0] = Task.Run(async () => {
                if (recognizeEmotions) {
                    try {
                        emotion = await RecognizeEmotionsAsync(client, photoAsByteArray);
                    }
                    catch (Exception e) {
                        log.Error("Error when using emotions api! Exception message: " + e.Message);
                    }
                }
            });
            tasks[1] = Task.Run(async () => {
                try {
                    faceAPIInfoDTO = await AnalyzeFacesAsync(client, photoAsByteArray);
                }
                catch (Exception e) {
                    log.Error("Error when using face api! Exception message: " + e.Message);
                }
            });
            Task.WaitAll(tasks);

            PhotoInfoDTO photoInfo = new PhotoInfoDTO {
                Age = faceAPIInfoDTO.Age,
                Emotion = emotion,
                FaceCountAsString = faceAPIInfoDTO.FaceCountAsString,
                Gender = faceAPIInfoDTO.Gender,
                FoundAndProcessedFaces = faceAPIInfoDTO.FoundAndProcessedFaces
            };

            return photoInfo;
        }

        public static async Task<Tuple<string, List<Dictionary<string, double>>>> RecognizeEmotionsAsync(HttpClient client, byte[] photoAsByteArray) {
            string emotionsAPIResponse = await PhotoAPICall(client, Config.EmotionAPIEndpoint, Config.EmotionAPIKey,
                photoAsByteArray);

            List<Dictionary<string, double>> allEmotions = new List<Dictionary<string, double>>();

            foreach (JToken rootToken in JArray.Parse(emotionsAPIResponse))
            {
                Dictionary<string, double> currentEmotions = new Dictionary<string, double>();
                JEnumerable<JToken> emotionsScoresList = rootToken.Last.First.Children();

                foreach (var emotionScore in emotionsScoresList) {
                    currentEmotions.Add(emotionScore.Value<JProperty>().Name, emotionScore.First.Value<double>());
                }
                allEmotions.Add(currentEmotions);
            }

            Dictionary<string, double> allEmotionsAggregated = new Dictionary<string, double>();
            foreach (string key in allEmotions[0].Keys){
                allEmotionsAggregated.Add(key, 0d);
            }

            foreach (Dictionary<string, double> dict in allEmotions) {
                foreach (string key in dict.Keys){
                    allEmotionsAggregated[key] += dict[key];
                }
            }

            double maximalScore = 0;
            string emotion = "";

            foreach (string key in allEmotionsAggregated.Keys) {
                if (allEmotionsAggregated[key] > maximalScore) {
                    maximalScore = allEmotionsAggregated[key];
                    emotion = key;
                }
            }
            

            return new Tuple<string, List<Dictionary<string, double>>>(emotion, allEmotions);
        }

        public static async Task<FaceAPIInfoDTO> AnalyzeFacesAsync(HttpClient client, byte[] photoAsByteArray){
            bool foundAndProcessedFaces = false;
            string faceCountAsString = "", gender = "", age = "";

            string requestParameters = string.Join("&", "returnFaceId=true", "returnFaceLandmarks=false",
                "returnFaceAttributes=age,gender");
            string uri = Config.FaceAPIEndpoint + "?" + requestParameters;
            string faceAPIResponse = await PhotoAPICall(client, uri, Config.FaceAPIKey, photoAsByteArray);

            JArray faces = JArray.Parse(faceAPIResponse);
            int males = 0, females = 0, faceCount = 0;
            double sumage = 0;
            List<Rectangle> faceRectangles = new List<Rectangle>();
            foreach (JToken face in faces) {
                faceCount++;

                if (face.SelectToken("faceAttributes").SelectToken("gender").ToString().Equals("male")) males++;
                else females++;

                sumage += face.SelectToken("faceAttributes").SelectToken("age").Value<double>();

                Rectangle faceRectangle = new Rectangle
                {
                    Width = face.SelectToken("faceRectangle").SelectToken("width").Value<int>(),
                    Height = face.SelectToken("faceRectangle").SelectToken("height").Value<int>(),
                    X = face.SelectToken("faceRectangle").SelectToken("left").Value<int>(),
                    Y = face.SelectToken("faceRectangle").SelectToken("top").Value<int>()
                };
                SingleFaceFaceAPIInfoDTO singleFaceInfoDto = new SingleFaceFaceAPIInfoDTO();

                singleFaceInfoDto.FaceRectangle = faceRectangle;
                singleFaceInfoDto.Age = face.SelectToken("faceAttributes").SelectToken("age")
                    .Value<double>();
                singleFaceInfoDto.Gender = face.SelectToken("faceAttributes").SelectToken("gender")
                    .ToString();
            }

            if (males == 0 && females > 0) gender = "F";
            if (males > 0 && females == 0) gender = "M";
            if (males > 0 && females > 0) gender = males > females ? "MF" : "FM";

            age = ((int)(sumage / faceCount)).ToString();

            if (faceCount > 0) foundAndProcessedFaces = true;

            faceCountAsString = faceCount.ToString();

            return new FaceAPIInfoDTO {
                Age = age,
                FaceCountAsString = faceCountAsString,
                Gender = gender,
                FoundAndProcessedFaces = foundAndProcessedFaces
            };
        }

        public static async Task<string> PhotoAPICall(HttpClient client, string endpoint, string apiKey,
            byte[] photoAsByteArray) {

            string responseContent;
            using (var content = new ByteArrayContent(photoAsByteArray)) {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
                HttpResponseMessage response = await client.PostAsync(endpoint, content);
                responseContent = response.Content.ReadAsStringAsync().Result;
            }

            return responseContent;
        }

        private static async Task SaveToDatabase(PhotoInfoDTO photoInfoDTO, TraceWriter log) {
            try {
                DocumentClient client = new DocumentClient(new Uri(Config.CosmosDBEndpoint), Config.CosmosDBKey);

                await client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(Config.CosmosDBDatabaseName,
                    Config.CosmosDBCollectionName), photoInfoDTO);
            }
            catch (DocumentClientException e) {
                if (e.StatusCode == HttpStatusCode.Conflict) {
                    string photoInfoDTOJson = JsonConvert.SerializeObject(photoInfoDTO);
                    log.Error($"Error while saving to db! Document with the same id already exists! " +
                        $"Did not save to db document: '{photoInfoDTOJson}'");
                }
                else {
                    log.Error($"Error while saving to db! Exception message: {e.Message}");
                }
            }
            catch (Exception e) {
                log.Error($"Error while saving to db! Exception message: {e.Message}");
            }
        }
    }


    class PhotoToProcessDTO {
        public byte[] PhotoAsByteArray { get; set; }
        public bool RecognizeEmotions { get; set; }
    }

    public class PhotoInfoDTO {
        public string FaceCountAsString { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
        public string Emotion { get; set; }
        public bool FoundAndProcessedFaces { get; set; }
    }

    public class FaceAPIInfoDTO {
        public string FaceCountAsString { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
        public bool FoundAndProcessedFaces { get; set; }
        public List<SingleFaceFaceAPIInfoDTO> SingleFacesInfo { get; set; }
    }

    public class SingleFaceFaceAPIInfoDTO {
        public double Age { get; set; }
        public string Gender { get; set; }
        public string Emotion { get; set; }
        public Rectangle FaceRectangle { get; set; }
    }
}
