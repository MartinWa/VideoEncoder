using System;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.MediaServices.Client;

namespace VideoEncoder
{
    static class VideoEncoder
    {
        private static readonly string MediaFiles = Path.GetFullPath(@"");
        private static readonly string SingleMp4File = Path.Combine(MediaFiles, @"small.mp4");
        private static MediaServicesCredentials _cachedCredentials;
        private static CloudMediaContext _context;

        // Media Services account information.
        private const string MediaServicesAccountName = @"zerodev"; // ConfigurationManager.AppSettings["MediaServicesAccountName"];
        private const string MediaServicesAccountKey = @"q6b6voUINwZpfIJZ82RI1ev4cM5SOmUSki9aM3Hx5cc="; //ConfigurationManager.AppSettings["MediaServicesAccountKey"];

        static void Main()
        {
            // Create and cache the Media Services credentials in a static class variable.
            _cachedCredentials = new MediaServicesCredentials(MediaServicesAccountName, MediaServicesAccountKey);

            // Use the cached credentials to create CloudMediaContext.
            _context = new CloudMediaContext(_cachedCredentials);

            // Use the SDK extension method to create a new asset by 
            // uploading a mezzanine file from a local path.
            IAsset asset = _context.Assets.CreateFromFile(SingleMp4File,
                AssetCreationOptions.None,
                (af, p) =>
                {
                    Console.WriteLine("Uploading '{0}' - Progress: {1:0.##}%", ((IAssetFile)p).Name, ((UploadProgressChangedEventArgs)p).Progress);
                });

            // Encode an MP4 file to a set of multibitrate MP4s.
            var assetMultibitrateMp4S = EncodeMp4ToMultibitrateMp4S(asset);

            // Publish the asset.
            _context.Locators.Create(LocatorType.OnDemandOrigin, assetMultibitrateMp4S, AccessPermissions.Read, TimeSpan.FromDays(30));

            // Get the URLs.
            Console.WriteLine("Smooth Streaming URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetSmoothStreamingUri().ToString());
            Console.WriteLine("MPEG DASH URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetMpegDashUri().ToString());
            Console.WriteLine("HLS URL:");
            Console.WriteLine(assetMultibitrateMp4S.GetHlsUri().ToString());
        }

        private static IAsset EncodeMp4ToMultibitrateMp4S(IAsset asset)
        {
            // Create a new job.
            var job = _context.Jobs.Create("Convert MP4 to Smooth Streaming.");

            // In Media Services, a media processor is a component that handles a specific processing task, 
            // such as encoding, format conversion, encrypting, or decrypting media content.
            //
            // Use the SDK extension method to  get a reference to the Windows Azure Media Encoder.
            IMediaProcessor encoder = _context.MediaProcessors.GetLatestMediaProcessorByName(MediaProcessorNames.WindowsAzureMediaEncoder);

            // Add task 1 - Encode single MP4 into multibitrate MP4s.
            var adpativeBitrateTask = job.Tasks.AddNew("MP4 to Adaptive Bitrate Task", encoder, "H264 Adaptive Bitrate MP4 Set 720p", TaskOptions.None);

            // Specify the input Asset
            adpativeBitrateTask.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job. 
            // This output is specified as AssetCreationOptions.None, which 
            // means the output asset is in the clear (unencrypted).
            adpativeBitrateTask.OutputAssets.AddNew("Multibitrate MP4s", AssetCreationOptions.None);

            // Submit the job and wait until it is completed.
            job.Submit();
            job = job.StartExecutionProgressTask(j =>
            {
                Console.WriteLine("Job state: {0}", ((IJob)j).State);
                Console.WriteLine("Job progress: {0:0.##}%", ((IJob)j).GetOverallProgress());
            }, CancellationToken.None).Result;

            // Get the output asset that contains the smooth stream.
            return job.OutputMediaAssets[0];
        }
    }
}