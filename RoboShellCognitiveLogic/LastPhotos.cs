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

namespace RoboShellCognitiveLogic
{
    public static class LastPhotos
    {
        [FunctionName("LastPhotos")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
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

                try {

                    var query = new TableQuery<PhotoInfoTableEntity>().Take(number);
                    //IEnumerable<PhotoInfoTableEntity> query = (from info in table.CreateQuery<PhotoInfoTableEntity>().Take(number)

                    var res = table.ExecuteQuery(query);
                    foreach (PhotoInfoTableEntity q in res) {
                        log.Info("=============================");
                        log.Info("Age: " + q.Age);
                        log.Info("Gender: " + q.Gender);

                        byte[] picture = GetPictureFromBlob(q.RowKey, "cropped-photos");
                        log.Info("Photo as bytearray length: " + picture.Length);


                        log.Info("=============================");
                    }
                }
                catch { }


                return req.CreateResponse(HttpStatusCode.OK, "you want " + number.ToString());
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

            byte[] res = new byte[blob.Properties.Length];
            for (int i = 0; i < res.Length; i++) {
                res[i] = 0x20; //what for?
            }
            blob.DownloadToByteArray(res, 0);

            return res;
        }


        private static void SavePhotoInfoToDatabase(string key, SingleFaceFaceAPIInfoDTO photoInfo, TraceWriter log) {
            string myAccountName = "roboshellstore"; //TODO
            string myAccountKey = "lGlfZMuRzmAnsecHA/st9Xrp/DGj+vtW9cvmeidAxfRz3kcSPuQAe9S63GPK/TmYhQBZnr3AotWEW1EIUbFXOg=="; //TODO
            StorageCredentials storageCredentials = new StorageCredentials(myAccountName, myAccountKey);
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
            CloudTableClient tableClient = cloudStorageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("photosInfo");

            PhotoInfoTableEntity photoInfoEntity = new PhotoInfoTableEntity(key, photoInfo);

            // Create the TableOperation object that inserts the customer entity.
            TableOperation insertOperation = TableOperation.Insert(photoInfoEntity);

            // Execute the insert operation.
            table.Execute(insertOperation);



            log.Info("SAVING PHOTO INFO TO DB");
        }

        private static void SavePhotoToDatabase(string key, byte[] photoAsByteArray, TraceWriter log) {
            string myAccountName = "roboshellstore"; //TODO
            string myAccountKey = "lGlfZMuRzmAnsecHA/st9Xrp/DGj+vtW9cvmeidAxfRz3kcSPuQAe9S63GPK/TmYhQBZnr3AotWEW1EIUbFXOg=="; //TODO
            StorageCredentials storageCredentials = new StorageCredentials(myAccountName, myAccountKey);
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);

            CloudBlobClient blobClient = cloudStorageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference("photos");

            CloudBlockBlob blob = container.GetBlockBlobReference(key + ".jpg");
            blob.UploadFromByteArrayAsync(photoAsByteArray, 0, photoAsByteArray.Length);

            log.Info("SAVING PHOTO TO DB");
        }

    }
}
