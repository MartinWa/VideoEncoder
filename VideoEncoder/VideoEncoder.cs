using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace VideoEncoder
{
    static class VideoEncoder
    {
        private static CloudMediaContext _mediaContext;
        private const string MediaServicesAccountName = @"zerodev"; // ConfigurationManager.AppSettings["MediaServicesAccountName"];
        private const string MediaServicesAccountKey = @"q6b6voUINwZpfIJZ82RI1ev4cM5SOmUSki9aM3Hx5cc="; //ConfigurationManager.AppSettings["MediaServicesAccountKey"];
        private const string MediaServicesStorageAccountName = @"zerodev";
        private const string MediaServicesStorageAccountKey = @"Cd9iLf0edc3zcmASfK/hj1KzIKGXJWgnmv5zKPBrAraGyaKDvKdyzyWZDaTE/I0N16dceMuX8pnqL84e3vQY0w==";
        static void Main()
        {
            var mediaServicesCredentials = new MediaServicesCredentials(MediaServicesAccountName, MediaServicesAccountKey);
            var storageCredentials = new StorageCredentials(MediaServicesStorageAccountName, MediaServicesStorageAccountKey);
            _mediaContext = new CloudMediaContext(mediaServicesCredentials);
            var storageAccount = new CloudStorageAccount(storageCredentials, true);
            var cloudBlobClient = storageAccount.CreateCloudBlobClient();
            var mediaBlobContainer = cloudBlobClient.GetContainerReference(cloudBlobClient.BaseUri + "mediafiles");
            var basePath = Path.GetFullPath(@"..\..\..");
            const string mediaFile = @"small.mp4";
            // var singleMp4File = Path.Combine(basePath, mediaFile);
            const string encodingProfileFile = @"profile.xml";
            var encodingProfile = "H264 Adaptive Bitrate MP4 Set 720p";
            try
            {
                encodingProfile = File.ReadAllText(Path.Combine(basePath, encodingProfileFile));
            }
            catch (Exception)
            {
                Console.WriteLine("No profile found at " );
                return;
            }

            // mediaBlobContainer.CreateIfNotExists();
            var blob = mediaBlobContainer.GetBlockBlobReference(mediaFile);
            // using (var stream = File.OpenRead(singleMp4File))
            // blob.UploadFromStream(stream);

            var asset = _mediaContext.Assets.Create("mediaAsset", AssetCreationOptions.None);
            var writePolicy = _mediaContext.AccessPolicies.Create("writePolicy", TimeSpan.FromMinutes(120), AccessPermissions.Write);
            var destinationLocator = _mediaContext.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);
            var uploadUri = new Uri(destinationLocator.Path);
            var assetContainerName = uploadUri.Segments[1];
            var assetContainer = cloudBlobClient.GetContainerReference(assetContainerName);
            var fileName = HttpUtility.UrlDecode(Path.GetFileName(blob.Uri.AbsoluteUri));
            var sourceCloudBlob = mediaBlobContainer.GetBlockBlobReference(fileName);
            sourceCloudBlob.FetchAttributes();
            if (sourceCloudBlob.Properties.Length > 0)
            {
                asset.AssetFiles.Create(fileName);
                var destinationBlob = assetContainer.GetBlockBlobReference(fileName);
                destinationBlob.DeleteIfExists();
                destinationBlob.StartCopyFromBlob(sourceCloudBlob);
                destinationBlob.FetchAttributes();
                if (sourceCloudBlob.Properties.Length != destinationBlob.Properties.Length)
                {
                    Console.WriteLine("Failed to copy");
                    return;
                }
            }
            destinationLocator.Delete();
            writePolicy.Delete();
            // ReSharper disable once ReplaceWithSingleCallToFirstOrDefault
            asset = _mediaContext.Assets.Where(a => a.Id == asset.Id).FirstOrDefault();
            if (asset == null)
            {
                Console.WriteLine("Asset is null");
                return;
            }
            Console.WriteLine("Ready to use " + asset.Name);

            // Encode an MP4 file to a set of multibitrate MP4s.
            var assetMultibitrateMp4S = EncodeMp4ToMultibitrateMp4S(asset, encodingProfile);

            // Publish the asset.
            _mediaContext.Locators.Create(LocatorType.OnDemandOrigin, assetMultibitrateMp4S, AccessPermissions.Read, TimeSpan.FromDays(30));

            // Get the URLs.
            Console.WriteLine("Smooth Streaming URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetSmoothStreamingUri().ToString());
            Console.WriteLine("MPEG DASH URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetMpegDashUri().ToString());
            Console.WriteLine("HLS URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetHlsUri().ToString());
        }

        private static IAsset EncodeMp4ToMultibitrateMp4S(IAsset asset, string profile)
        {
            // Create a new job.
            var job = _mediaContext.Jobs.Create("Convert MP4 to Smooth Streaming.");

            // In Media Services, a media processor is a component that handles a specific processing task, 
            // such as encoding, format conversion, encrypting, or decrypting media content.
            //
            // Use the SDK extension method to  get a reference to the Windows Azure Media Encoder.
            var encoder = _mediaContext.MediaProcessors.GetLatestMediaProcessorByName(MediaProcessorNames.WindowsAzureMediaEncoder);
            // Add task 1 - Encode single MP4 into multibitrate MP4s.
            var task = job.Tasks.AddNew("Encoding " + asset.Name + " to custom profile", encoder, profile, TaskOptions.None);

            // Specify the input Asset
            task.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job. 
            // This output is specified as AssetCreationOptions.None, which 
            // means the output asset is in the clear (unencrypted).
            task.OutputAssets.AddNew(asset.Name + " as custom profile", AssetCreationOptions.None);

            // Submit the job and wait until it is completed.
            job.Submit();
            job = job.StartExecutionProgressTask(j =>
            {
                Console.WriteLine("Job state: {0}", j.State);
                Console.WriteLine("Job progress: {0:0.##}%", j.GetOverallProgress());
            }, CancellationToken.None).Result;

            // Get the output asset that contains the smooth stream.
            return job.OutputMediaAssets[0];
        }
    }
}