using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
// ReSharper disable SpecifyACultureInStringConversionExplicitly
// ReSharper disable StringCompareIsCultureSpecific.3

namespace RoboShellCognitiveLogic
{
    public static class LastPhotos
    {
        [FunctionName("LastPhotos")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]
            HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            string numberAsString = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "number", true) == 0)
                .Value;

            if (numberAsString != null) {
                int number = int.Parse(numberAsString);

                string myAccountName = "roboshellstore"; //TODO
                string myAccountKey = "lGlfZMuRzmAnsecHA/st9Xrp/DGj+vtW9cvmeidAxfRz3kcSPuQAe9S63GPK/TmYhQBZnr3AotWEW1EIUbFXOg=="; //TODO
                StorageCredentials storageCredentials = new StorageCredentials(myAccountName, myAccountKey);
                CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
                CloudTableClient tableClient = cloudStorageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("croppedPhotosMeta");

                List<PhotoMetaInfoDTO> li = new List<PhotoMetaInfoDTO>();

                try {
                    var query = new TableQuery<PhotoInfoTableEntity>().Take(number);

                    var res = table.ExecuteQuery(query);
                    foreach (PhotoInfoTableEntity q in res) {
                        byte[] picture = GetPictureFromBlob(q.RowKey, "cropped-photos");
                        li.Add(new PhotoMetaInfoDTO {
                            Age = q.Age.ToString(),
                            Gender = q.Gender,
                            Photo = Convert.ToBase64String(picture)
                        });
                    }
                }
                catch {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Something went wrong");
                }

                var json = JsonConvert.SerializeObject(li);
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                return response;
            }
            else {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass number on the query string");
            }
        }

        private static byte[] GetPictureFromBlob(string key, string containerName) {
            string myAccountName = "roboshellstore"; //TODO
            string myAccountKey = "lGlfZMuRzmAnsecHA/st9Xrp/DGj+vtW9cvmeidAxfRz3kcSPuQAe9S63GPK/TmYhQBZnr3AotWEW1EIUbFXOg=="; //TODO
            StorageCredentials storageCredentials = new StorageCredentials(myAccountName, myAccountKey);
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);

            CloudBlobClient blobClient = cloudStorageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

            CloudBlockBlob blob = container.GetBlockBlobReference(key + ".jpg");
            blob.FetchAttributes();
            byte[] res = new byte[blob.Properties.Length];
            for (int i = 0; i < res.Length; i++) {
                res[i] = 0x20; //TODO: what for?
            }
            blob.DownloadToByteArray(res, 0);

            return res;
        }
    }

    public class PhotoMetaInfoDTO {
        public string Gender { get; set; }
        public string Age { get; set; }
        public string Photo { get; set; }
    }
}
