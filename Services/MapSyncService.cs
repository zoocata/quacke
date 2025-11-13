using Newtonsoft.Json;
using QuakeServerManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace QuakeServerManager.Services
{
    /// <summary>
    /// Service for managing map repository synchronization.
    /// Fetches manifest.json from GitHub and tracks available community maps.
    /// </summary>
    public class MapSyncService
    {
        private readonly HttpClient _httpClient;
        private const string ManifestFileName = "manifest.json";

        public MapSyncService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuackeManager/1.0");
        }

        /// <summary>
        /// Fetches the latest manifest.json from the GitHub map repository.
        /// </summary>
        /// <param name="repositoryUrl">GitHub repository URL (e.g., https://github.com/username/quacke-maps)</param>
        /// <returns>The parsed MapManifest, or null if fetch fails</returns>
        public async Task<MapManifest?> FetchManifestAsync(string repositoryUrl)
        {
            try
            {
                // Convert GitHub repo URL to raw content URL
                // Example: https://github.com/user/repo -> https://raw.githubusercontent.com/user/repo/main/manifest.json
                var rawUrl = ConvertToRawGitHubUrl(repositoryUrl, ManifestFileName);

                var response = await _httpClient.GetAsync(rawUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var manifest = JsonConvert.DeserializeObject<MapManifest>(json);

                return manifest;
            }
            catch (HttpRequestException ex)
            {
                // Log or handle HTTP errors (e.g., network issues, 404)
                Console.WriteLine($"Failed to fetch manifest: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                // Log or handle JSON parsing errors
                Console.WriteLine($"Failed to parse manifest: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compares local manifest version with remote version to detect updates.
        /// </summary>
        /// <param name="localVersion">Current local version string</param>
        /// <param name="remoteManifest">Fetched remote manifest</param>
        /// <returns>True if remote version is newer</returns>
        public bool IsUpdateAvailable(string localVersion, MapManifest? remoteManifest)
        {
            if (remoteManifest == null) return false;
            if (string.IsNullOrEmpty(localVersion)) return true;

            try
            {
                var local = new Version(localVersion);
                var remote = new Version(remoteManifest.Version);
                return remote > local;
            }
            catch (Exception)
            {
                // If version parsing fails, assume update is available to be safe
                return true;
            }
        }

        /// <summary>
        /// Downloads a specific map file from the repository.
        /// </summary>
        /// <param name="repositoryUrl">GitHub repository URL</param>
        /// <param name="mapFileName">Name of the .bsp file to download</param>
        /// <param name="destinationPath">Local path to save the map</param>
        /// <param name="progress">Optional progress callback (0-100)</param>
        /// <returns>True if download succeeds</returns>
        public async Task<bool> DownloadMapAsync(string repositoryUrl, string mapFileName, string destinationPath, IProgress<int>? progress = null)
        {
            try
            {
                // Convert to raw GitHub URL for the map file
                var rawUrl = ConvertToRawGitHubUrl(repositoryUrl, $"maps/{mapFileName}");

                using var response = await _httpClient.GetAsync(rawUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    if (progress != null && totalBytes > 0)
                    {
                        var percentComplete = (int)((downloadedBytes * 100) / totalBytes);
                        progress.Report(percentComplete);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download map {mapFileName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads all maps from the manifest that don't exist locally.
        /// </summary>
        /// <param name="repositoryUrl">GitHub repository URL</param>
        /// <param name="manifest">The manifest containing map information</param>
        /// <param name="destinationDirectory">Directory to save maps</param>
        /// <param name="overallProgress">Progress callback for overall operation</param>
        /// <returns>Number of maps successfully downloaded</returns>
        public async Task<int> DownloadAllMapsAsync(string repositoryUrl, MapManifest manifest, string destinationDirectory, IProgress<int>? overallProgress = null)
        {
            Directory.CreateDirectory(destinationDirectory);

            int totalMaps = manifest.Maps.Count;
            int downloadedCount = 0;

            for (int i = 0; i < totalMaps; i++)
            {
                var map = manifest.Maps[i];
                var destinationPath = Path.Combine(destinationDirectory, map.FileName);

                // Skip if file already exists and size matches
                if (File.Exists(destinationPath))
                {
                    var fileInfo = new FileInfo(destinationPath);
                    if (fileInfo.Length == map.FileSizeBytes)
                    {
                        downloadedCount++;
                        overallProgress?.Report((i + 1) * 100 / totalMaps);
                        continue;
                    }
                }

                // Download the map
                var success = await DownloadMapAsync(repositoryUrl, map.FileName, destinationPath);
                if (success)
                {
                    downloadedCount++;
                }

                overallProgress?.Report((i + 1) * 100 / totalMaps);
            }

            return downloadedCount;
        }

        /// <summary>
        /// Converts a GitHub repository URL to raw content URL.
        /// </summary>
        /// <param name="repoUrl">GitHub repo URL (e.g., https://github.com/user/repo)</param>
        /// <param name="filePath">Path to file within repo</param>
        /// <returns>Raw GitHub URL for direct file access</returns>
        private string ConvertToRawGitHubUrl(string repoUrl, string filePath)
        {
            // Remove trailing slash if present
            repoUrl = repoUrl.TrimEnd('/');

            // Extract user and repo name from URL
            // Example: https://github.com/username/repo-name -> username/repo-name
            var githubPrefix = "https://github.com/";
            if (!repoUrl.StartsWith(githubPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid GitHub repository URL. Must start with https://github.com/");
            }

            var userAndRepo = repoUrl.Substring(githubPrefix.Length);

            // Construct raw URL: https://raw.githubusercontent.com/user/repo/main/{filePath}
            return $"https://raw.githubusercontent.com/{userAndRepo}/main/{filePath}";
        }

        /// <summary>
        /// Calculates SHA256 hash of a file for integrity verification.
        /// </summary>
        public static string CalculateFileHash(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var fileStream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(fileStream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
