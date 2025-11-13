using QuakeServerManager.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuakeServerManager.Services.Ssh
{
    public class FileTransferService
    {
        public event Action<string, LogLevel>? LogMessage;

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        public async Task UploadFileAsync(SftpClient sftpClient, string localPath, string remotePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sftpClient.UploadFile(fileStream, remotePath);
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip files that we don't have permission to read
                    throw new UnauthorizedAccessException($"Access denied to file: {localPath}");
                }
                catch (IOException ex) when (ex.Message.Contains("denied"))
                {
                    // Handle other access denied scenarios
                    throw new UnauthorizedAccessException($"Access denied to file: {localPath}");
                }
            });
        }

        public async Task UploadTextAsync(SftpClient sftpClient, string content, string remotePath)
        {
            await Task.Run(() =>
            {
                sftpClient.WriteAllText(remotePath, content, Encoding.UTF8);
            });
        }

        public async Task UploadDirectoryAsync(SftpClient sftpClient, string localPath, string remotePath, Action<double, string>? progressCallback = null, double startProgress = 0.0, double endProgress = 100.0)
        {
            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
                return;

            // Step 1: Collect all unique directory paths
            var allRemoteDirs = new HashSet<string>();
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(localPath, file);
                var remoteFilePath = Path.Combine(remotePath, relativePath).Replace('\\', '/');
                var remoteDir = Path.GetDirectoryName(remoteFilePath);
                if (remoteDir != null)
                {
                    allRemoteDirs.Add(remoteDir);
                }
            }

            // Step 2: Create directories sequentially to avoid race conditions.
            // Sorting by length ensures parent directories are created before child directories.
            foreach (var dir in allRemoteDirs.OrderBy(d => d.Length))
            {
                CreateDirectoryRecursive(sftpClient, dir);
            }

            // Step 3: Upload files in parallel
            double totalFiles = files.Length;
            long filesUploaded = 0;
            var semaphore = new SemaphoreSlim(10); // Limit concurrency

            var uploadTasks = new List<Task>();
            foreach (var file in files)
            {
                await semaphore.WaitAsync();

                uploadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                        var remoteFilePath = Path.Combine(remotePath, relativePath).Replace('\\', '/');
                        await UploadFileAsync(sftpClient, file, remoteFilePath);

                        var uploadedCount = Interlocked.Increment(ref filesUploaded);
                        var progress = startProgress + ((endProgress - startProgress) * (uploadedCount / totalFiles));
                        progressCallback?.Invoke(progress, $"Uploaded {relativePath}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log($"Skipping file {file} - access denied: {ex.Message}", LogLevel.Warning);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error uploading file {file} to {remotePath}. Exception: {ex.Message}", LogLevel.Error);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(uploadTasks);
        }

        private void CreateDirectoryRecursive(SftpClient client, string path)
        {
            string current = "";
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Handle absolute paths
            if (path.StartsWith("/"))
            {
                current = "/";
            }

            foreach (var part in parts)
            {
                current = Path.Combine(current, part).Replace('\\', '/');
                if (!client.Exists(current))
                {
                    client.CreateDirectory(current);
                }
            }
        }
    }
}
