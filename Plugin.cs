using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System.Threading.Tasks;
using YoutubeDLSharp;
using System.IO;
using UnityEngine.InputSystem;
using System.Net.Http;
using System;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.ComponentModel;
using System.Net;
using static System.Net.WebRequestMethods;
using YoutubeDLSharp.Options;
using BestestTVModPlugin;

namespace LethalCompanyTemplate
{
    [BepInPlugin("DeathWrench.YoutubeDownloader", "YoutubeDownloader", "0.0.1")]
    [BepInDependency("DeathWrench.BestestTelevisionMod", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> playlistModeActivated;
        private ConfigEntry<Key> downloadKeyBind;
        private ConfigEntry<string> defaultVideoUrl;
        private ConfigEntry<int> startOfPlaylist;
        private ConfigEntry<int> endOfPlaylist;
        public static ManualLogSource Log = new ManualLogSource("YoutubeDownloader");
        private static readonly Harmony Harmony = new Harmony("DeathWrench.YoutubeDownloader");

        private YoutubeDL ytdl;

        private InputAction downloadAction;

        static string pluginPath = Paths.PluginPath + Path.DirectorySeparatorChar.ToString() + "DeathWrench-YoutubeDownloader";
        static string libraryPath = Path.Combine(pluginPath, "lib");
        string ytDlpPath = Path.Combine(libraryPath, "yt-dlp.exe");
        //string ffmpegPath = Path.Combine(libraryPath, "ffmpeg.exe");
        string televisionVideosPath = Path.Combine(pluginPath, "Television Videos");
        private async void Start()
        {
            Log.LogInfo($"Plugin {"DeathWrench.YoutubeDownloader"} is loaded!");

            playlistModeActivated = Config.Bind("Settings", "Playlist Mode", false, "Download playlists?");
            downloadKeyBind = Config.Bind("Settings", "Download Keybind", Key.Semicolon, "Which key to press to initiate YouTube video download.");
            defaultVideoUrl = Config.Bind("Settings", "Default Video URL", "https://www.youtube.com/watch?v=bq9ghmgqoyc", "Default YouTube video URL for downloading.");
            startOfPlaylist = Config.Bind("Settings", "Start of Playlist", 1, "Select specific videos in a playlist to download. Setting to 5 will download the 5th video in the playlist.");
            endOfPlaylist = Config.Bind("Settings", "End of Playlist", 999999, "Which video to stop downloading at. Setting to 7 with start set to 5 will only download the 5th,6th, and 7th videos.");

            // Create Television Videos folder if it doesn't exist
            if (!Directory.Exists(televisionVideosPath))
            {
                Directory.CreateDirectory(televisionVideosPath);
            }

            if (!Directory.Exists(libraryPath))
            {
                Directory.CreateDirectory(libraryPath);
            }

            // Check if yt-dlp already exists
            if (!System.IO.File.Exists($"{libraryPath}\\yt-dlp.exe"))
            {
                // Download yt-dlp
                await DownloadLatestYtDlpRelease($"{libraryPath}\\yt-dlp.exe");
            }
            else
            {
                Log.LogInfo("yt-dlp already exists. Skipping download.");
            }

            // Check if FFmpeg already exists
            if (!System.IO.File.Exists($"{libraryPath}\\ffmpeg.exe"))
            {
                // Download and extract FFmpeg
                await DownloadAndExtractFFmpeg($"{libraryPath}\\ffmpeg.exe");
            }
            else
            {
                Log.LogInfo("FFmpeg already exists. Skipping download.");
            }

            // Initialize YoutubeDL object
            ytdl = new YoutubeDL();
            ytdl.YoutubeDLPath = $"{libraryPath}\\yt-dlp.exe";
            ytdl.FFmpegPath = $"{libraryPath}\\ffmpeg.exe";

            var downloadKey = downloadKeyBind.Value;
            // Define an action for the download key
            downloadAction = new InputAction(binding: $"<Keyboard>/{downloadKey}", interactions: "press");

            // Bind the action to the key
            downloadAction.Enable();

            // Subscribe to the action's performed event
            downloadAction.performed += async ctx =>
            {
                // Inside the event handler, check if the action was performed
                if (ctx.ReadValueAsButton())
                {
                    // Get the size of the video to be downloaded
                    long videoSize = await GetVideoSize(defaultVideoUrl.Value);

                    // Check available disk space
                    if (CheckDiskSpace(televisionVideosPath, videoSize))
                    {
                        // Display download progress message
                        HUDManager.Instance.DisplayTip("Download in Progress", "Please wait while the video is being downloaded...", false, false, "DownloadProgressTip");

                        // Download video
                        await DownloadVideo(defaultVideoUrl.Value, televisionVideosPath);
                    }
                    else
                    {
                        // Display error message if there's not enough disk space
                        HUDManager.Instance.DisplayTip("Download Error", "Not enough free disk space to download the video.", false, false, "DownloadErrorTip");
                    }
                }
            };
            Harmony.PatchAll();
        }

        private async Task DownloadLatestYtDlpRelease(string destinationFolder)
        {
            // Send a GET request to the latest release URL
            string latestReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(latestReleaseUrl);
            request.AllowAutoRedirect = true;

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    // Get the final URL after redirection
                    string redirectedUrl = response.ResponseUri.AbsoluteUri;

                    // Extract the release date or version from the redirected URL
                    string[] urlParts = redirectedUrl.Split('/');
                    string releaseDateOrVersion = urlParts[urlParts.Length - 1];

                    // Construct the download URL
                    string downloadUrl = $"https://github.com/yt-dlp/yt-dlp/releases/download/{releaseDateOrVersion}/yt-dlp.exe";

                    // Download the file
                    string downloadPath = ytDlpPath;
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadFile(downloadUrl, downloadPath);
                        Log.LogInfo("Download completed successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error: {ex.Message}");
            }
        }

        private async Task DownloadAndExtractFFmpeg(string ffmpegPath)
        {
            Log.LogInfo("Downloading FFmpeg...");

            // URL for the latest release tag
            string releaseUrl = "https://github.com/GyanD/codexffmpeg/releases/latest/";

            // Request to get the latest release URL
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(releaseUrl);
            request.AllowAutoRedirect = true;

            string actualReleaseUrl;

            // Get the actual release URL after redirection
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    actualReleaseUrl = response.ResponseUri.AbsoluteUri;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error getting release URL: {ex.Message}");
                return;
            }

            // Extract the release version from the URL
            string[] urlParts = actualReleaseUrl.Split('/');
            string releaseVersion = urlParts[urlParts.Length - 1];

            // Construct the download URL
            string ffmpegZipUrl = $"https://github.com/GyanD/codexffmpeg/releases/download/{releaseVersion}/ffmpeg-{releaseVersion}-essentials_build.zip";
            string tempZipPath = Path.Combine(pluginPath, $"ffmpeg-{releaseVersion}-essentials_build.zip");

            // Download the zip file
            using (WebClient client = new WebClient())
            {
                await client.DownloadFileTaskAsync(ffmpegZipUrl, tempZipPath);
            }

            // Extract the zip file
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, pluginPath);
                Log.LogInfo("FFmpeg downloaded and extracted successfully.");

                // Get the path of the bin directory
                string binDirectory = Path.Combine(pluginPath, $"ffmpeg-{releaseVersion}-essentials_build", "bin");

                // Move all files from the bin directory to the specified location
                foreach (string filePath in Directory.GetFiles(binDirectory))
                {
                    string fileName = Path.GetFileName(filePath);
                    string destinationPath = Path.Combine(libraryPath, fileName);
                    System.IO.File.Move(filePath, destinationPath);
                    Log.LogInfo($"FFmpeg binary moved to: {destinationPath}");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to extract FFmpeg zip: {ex.Message}");
            }

            // Cleanup: delete the temporary zip file and extracted directory
            try
            {
                System.IO.File.Delete(tempZipPath);
                Directory.Delete(Path.Combine(pluginPath, $"ffmpeg-{releaseVersion}-essentials_build"), true);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to delete temporary files: {ex.Message}");
            }
        }

        private async Task<long> GetVideoSize(string videoUrl)
        {
            using (HttpClient client = new HttpClient())
            {
                var headRequest = new HttpRequestMessage(HttpMethod.Head, videoUrl);
                var response = await client.SendAsync(headRequest);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                {
                    long videoSize = response.Content.Headers.ContentLength.Value;
                    Log.LogInfo($"Video size obtained: {videoSize} bytes");
                    return videoSize;
                }
                else
                {
                    // Handle error or return a default size
                    Log.LogError("Failed to obtain video size. Default size set to 0 bytes");
                    return 0;
                }
            }
        }

        private bool CheckDiskSpace(string path, long videoSize)
        {
            DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(path));
            long freeSpaceBytes = driveInfo.AvailableFreeSpace;
            bool hasEnoughSpace = freeSpaceBytes >= videoSize;
            if (!hasEnoughSpace)
            {
                Log.LogWarning("Not enough free disk space to download the video.");
                Log.LogWarning($"Required space: {videoSize} bytes, Available space: {freeSpaceBytes} bytes");
            }
            else
            {
                Log.LogInfo("Sufficient disk space available for download.");
                Log.LogInfo($"Required space: {videoSize} bytes, Available space: {freeSpaceBytes} bytes");
            }
            return hasEnoughSpace;
        }

        private async Task DownloadVideo(string videoUrl, string destinationFolder)
        {
            Log.LogInfo("Downloading video...");
            // Set the output folder to the specified destination folder
            ytdl.OutputFolder = destinationFolder;

            var options = new OptionSet()
            {
                //NoContinue = true,
                //RestrictFilenames = true,
                Format = "best",
                RecodeVideo = VideoRecodeFormat.Mp4,
                //Exec = "echo {}"
            };
            if (playlistModeActivated.Value)
            {
                var res = await ytdl.RunVideoPlaylistDownload(
                videoUrl, overrideOptions: options,
                start: startOfPlaylist.Value, end: endOfPlaylist.Value);

                // Display a message indicating that the download is complete
                HUDManager.Instance.DisplayTip("Download Complete", "The playlist has been successfully downloaded.", false, false, "DownloadpCompleteTip");
            }
            else // Download a video
            {
                var res = await ytdl.RunVideoDownload(videoUrl, overrideOptions: options);
                // The path of the downloaded file
                string path = res.Data;
                Log.LogInfo($"Video downloaded at: {path}");

                // Display a message indicating that the download is complete
                HUDManager.Instance.DisplayTip("Download Complete", "The video has been successfully downloaded.", false, false, "DownloadvCompleteTip");
            }

            // After downloading, reset the list of videos and load them again
            VideoManager.Videos.Clear();
            VideoManager.Load();
        }

        private void OnDestroy()
        {
            // Disable the download action when the plugin is destroyed or disabled
            downloadAction.Disable();
            Log.LogInfo("YoutubeDownloader plugin destroyed or disabled. Download action disabled.");
        }
    }
}