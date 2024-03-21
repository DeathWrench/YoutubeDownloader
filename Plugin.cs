using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using System.Threading.Tasks;
using YoutubeDLSharp;
using System.IO;
using UnityEngine.InputSystem;
using System.Net.Http;
using System;
using System.Net;
using YoutubeDLSharp.Options;
using BestestTVModPlugin;
using YoutubeDLSharp.Metadata;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LethalCompanyTemplate
{
    [BepInPlugin("DeathWrench.YoutubeDownloader", "\u200bYoutubeDownloader", "0.0.1")]
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
            defaultVideoUrl = Config.Bind("Settings", "Default Video URL", "https://www.youtube.com/watch?v=4Nty0riqSOs", "Default YouTube video URL for downloading.");
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
                        if (!playlistModeActivated.Value)
                        {
                            // Display download progress message
                            HUDManager.Instance.DisplayTip("Download in Progress", "Please wait while the video is being downloaded...", false, false, "DownloadProgressTip");

                            // Download video
                            await DownloadVideo(defaultVideoUrl.Value, televisionVideosPath);
                        }
                        else
                        {
                            // Display download progress message
                            HUDManager.Instance.DisplayTip("Download in Progress", "Please wait while the playlist is being downloaded...", false, false, "DownloadProgressTip");

                            // Download playlist
                            await DownloadPlaylist(defaultVideoUrl.Value, televisionVideosPath);
                        }
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

        private void Update()
        {
            // Check for configuration changes
            if (CheckForConfigChanges())
            {
                // Configuration has changed, update settings
                UpdateSettings();
            }

            // Check for new files
            if (CheckForNewFiles())
            {
                // New files detected, handle them
                HandleNewFiles();
            }
            // Use Input System to check for key press
            if (Keyboard.current.ctrlKey.isPressed && Keyboard.current.vKey.wasPressedThisFrame)
            {
                // Handle paste operation from clipboard
                PasteTextFromClipboard();
            }
            return;
        }

        private void PasteTextFromClipboard()
        {
            string clipboardText = GUIUtility.systemCopyBuffer;

            // Perform the paste operation based on your game's requirements
            // For example, paste the text into a UI input field
            Debug.Log("Pasting text from clipboard: " + clipboardText);
        }

        private bool CheckForConfigChanges()
        {
            // Check if any of the config entries have changed
            if (playlistModeActivated.Value != Config.Bind<bool>("Settings", "Playlist Mode", playlistModeActivated.Value).Value ||
                downloadKeyBind.Value != Config.Bind<Key>("Settings", "Download Keybind", downloadKeyBind.Value).Value ||
                defaultVideoUrl.Value != Config.Bind<string>("Settings", "Default Video URL", defaultVideoUrl.Value).Value ||
                startOfPlaylist.Value != Config.Bind<int>("Settings", "Start of Playlist", startOfPlaylist.Value).Value ||
                endOfPlaylist.Value != Config.Bind<int>("Settings", "End of Playlist", endOfPlaylist.Value).Value)
            {
                // Config has changed
                return true;
            }

            return false;
        }

        private void UpdateSettings()
        {
            // Update settings with new values from config
            playlistModeActivated.Value = Config.Bind<bool>("Settings", "Playlist Mode", playlistModeActivated.Value).Value;
            downloadKeyBind.Value = Config.Bind<Key>("Settings", "Download Keybind", downloadKeyBind.Value).Value;
            defaultVideoUrl.Value = Config.Bind<string>("Settings", "Default Video URL", defaultVideoUrl.Value).Value;
            startOfPlaylist.Value = Config.Bind<int>("Settings", "Start of Playlist", startOfPlaylist.Value).Value;
            endOfPlaylist.Value = Config.Bind<int>("Settings", "End of Playlist", endOfPlaylist.Value).Value;
        }

        private bool CheckForNewFiles()
        {
            // Check for new files in the destination folder
            string[] files = Directory.GetFiles(televisionVideosPath, "*.mp4");
            if (files.Length > VideoManager.Videos.Count)
            {
                // New files detected
                return true;
            }

            return false;
        }

        private void HandleNewFiles()
        {
            // Reset the list of videos and load them again
            VideoManager.Videos.Clear();
            VideoManager.Load();
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

        private async Task<bool> IsVideoAlreadyDownloaded(string videoUrl, string destinationFolder)
        {
            // Fetch video metadata without downloading
            var res = await ytdl.RunVideoDataFetch(videoUrl);

            if (res == null)
            {
                Log.LogError($"Failed to fetch video metadata for URL: {videoUrl}");
                return false; // Unable to determine, assuming not downloaded
            }

            // Get video metadata
            VideoData video = res.Data;
            string title = video.Title;

            // Generate the filename from the video metadata
            string videoId = video.ID;
            string fileNamePattern = $@"\[{Regex.Escape(videoId)}\]\.mp4"; // Escaping videoId to avoid regex injection
            Regex regex = new Regex(fileNamePattern);

            // Check files in the destination folder
            string[] files = Directory.GetFiles(destinationFolder);
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                if (regex.IsMatch(fileName))
                {
                    Log.LogInfo($"Video '{title}' already exists. Skipping download.");
                    return true; // Video already exists
                }
            }

            return false; // Video does not exist
        }


        /*private async Task<List<string>> GetVideoIdsFromPlaylist(string playlistUrl)
        {
            List<string> videoIds = new List<string>();

            // Fetch the HTML content of the playlist page
            string htmlContent = await DownloadHtmlContent(playlistUrl);

            if (htmlContent != null)
            {
                // Use regular expressions to extract video IDs from the HTML content
                MatchCollection matches = Regex.Matches(htmlContent, @"""videoId"":""([^""]+)""");

                foreach (Match match in matches)
                {
                    string videoId = match.Groups[1].Value;
                    videoIds.Add(videoId);
                }
            }

            return videoIds;
        }

        private async Task<string> DownloadHtmlContent(string url)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    string htmlContent = await response.Content.ReadAsStringAsync();
                    return htmlContent;
                }
            }
            catch (HttpRequestException ex)
            {
                Log.LogError($"Error downloading HTML content from {url}: {ex.Message}");
                return null;
            }
        }
        private async Task<bool> IsPlaylistAlreadyDownloaded(string playlistUrl, string destinationFolder)
        {
            List<string> videoIds = await GetVideoIdsFromPlaylist(playlistUrl);
            bool playlistDownloaded = false;

            if (videoIds.Count == 0)
            {
                Log.LogError($"Failed to fetch video IDs from playlist URL: {playlistUrl}");
                HUDManager.Instance.DisplayTip("Download Error", "Failed to fetch video IDs from the playlist URL.", false, false, "DownloadErrorTip");
                return false; // Unable to determine, assuming not downloaded
            }

            foreach (string videoId in videoIds)
            {
                string fileNamePattern = $@"\[{Regex.Escape(videoId)}\]\.mp4"; // Escaping videoId to avoid regex injection
                Regex regex = new Regex(fileNamePattern);

                // Check if any file in the destination folder corresponds to the video ID
                bool videoExists = false;
                string[] files = Directory.GetFiles(destinationFolder);
                foreach (string filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);
                    if (regex.IsMatch(fileName))
                    {
                        videoExists = true;
                        break; // Found a matching file, no need to continue checking
                    }
                }

                // If the video exists, display a tip and skip the download
                if (videoExists)
                {
                    Log.LogInfo($"Video with ID '{videoId}' already exists. Skipping download.");
                    HUDManager.Instance.DisplayTip("Download Skipped", $"Video with ID '{videoId}' already exists. Skipping download.", false, false, "DownloadSkippedTip");
                }
                else
                {
                    // Video doesn't exist, mark the playlist as not fully downloaded
                    playlistDownloaded = false;
                }
            }

            // If all videos exist, mark the playlist as fully downloaded
            if (playlistDownloaded)
            {
                HUDManager.Instance.DisplayTip("Download Complete", "All videos in the playlist are already downloaded.", false, false, "DownloadCompleteTip");
            }
            else
            {
                HUDManager.Instance.DisplayTip("Download Incomplete", "Some videos in the playlist are missing and need to be downloaded.", false, false, "DownloadIncompleteTip");
            }

            return playlistDownloaded;
        }*/


        private async Task DownloadPlaylist(string playlistUrl, string destinationFolder)
        {
            Log.LogInfo("Downloading video...");

            // Check if the video is already downloaded
            bool alreadyDownloaded = await IsVideoAlreadyDownloaded(playlistUrl, destinationFolder);
            if (alreadyDownloaded)
            {
                // Display a message indicating that the video is already downloaded
                HUDManager.Instance.DisplayTip("Download Skipped", "The video is already downloaded.", false, false, "DownloadSkippedTip");
                return;
            }

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

            var res = await ytdl.RunVideoPlaylistDownload(
            playlistUrl, overrideOptions: options,
            start: startOfPlaylist.Value, end: endOfPlaylist.Value
               );

            if (res == null)
            {
                // Display an error message if the download fails
                HUDManager.Instance.DisplayTip("Download Error", "Failed to download the video.", false, false, "DownloadErrorTip");
                return;
            }

            // Display a message indicating that the download is complete
            HUDManager.Instance.DisplayTip("Download Complete", "The video has been successfully downloaded.", false, false, "DownloadCompleteTip");


            // After downloading, reset the list of videos and load them again
            VideoManager.Videos.Clear();
            VideoManager.Load();
        }
        private async Task DownloadVideo(string videoUrl, string destinationFolder)
        {
            Log.LogInfo("Downloading video...");

            // Check if the video is already downloaded
            bool alreadyDownloaded = await IsVideoAlreadyDownloaded(videoUrl, destinationFolder);
            if (alreadyDownloaded)
            {
                // Display a message indicating that the video is already downloaded
                HUDManager.Instance.DisplayTip("Download Skipped", "The video is already downloaded.", false, false, "DownloadSkippedTip");
                return;
            }

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

                var res = await ytdl.RunVideoDownload(videoUrl, overrideOptions: options);

                if (res == null)
                {
                    // Display an error message if the download fails
                    HUDManager.Instance.DisplayTip("Download Error", "Failed to download the video.", false, false, "DownloadErrorTip");
                    return;
                }

                // Display a message indicating that the download is complete
                HUDManager.Instance.DisplayTip("Download Complete", "The video has been successfully downloaded.", false, false, "DownloadCompleteTip");
            

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
