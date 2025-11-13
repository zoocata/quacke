using QuakeServerManager.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuakeServerManager.Services.Ssh
{
    /// <summary>
    /// Handles Docker-based deployment flow:
    /// 1. Ensures Docker & docker-compose are installed on the VPS.
    /// 2. Uploads a build context containing Dockerfile, pak0.pk3 (first deploy only) and per-instance server.cfg files.
    /// 3. Builds the derived image and starts containers via docker-compose.
    /// </summary>
    public class DockerDeploymentService
    {
        private readonly ConnectionService _connectionService;
        private readonly FileTransferService _fileTransferService;

        public event Action<string, LogLevel>? LogMessage;

        public DockerDeploymentService(ConnectionService connectionService, FileTransferService fileTransferService)
        {
            _connectionService = connectionService;
            _fileTransferService = fileTransferService;
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        /// <summary>
        /// Deploys (or updates) all server instances for the given connection using Docker.
        /// Single-image architecture - no manual CLI steps required.
        /// </summary>
        /// <param name="connection">Target VPS connection.</param>
        /// <param name="instances">Server instances to deploy.</param>
        /// <param name="pak0Path">Local path to pak0.pk3.</param>
        /// <param name="cpmaPath">Local path to CPMA folder.</param>
        /// <param name="serverExecutablePath">Local path to server executable (cnq3-server-x64).</param>
        /// <param name="mapsPath">Optional local path to maps folder.</param>
        /// <param name="customMaps">Optional custom map files to include</param>
        /// <param name="progressCallback">Progress callback (0-100, message)</param>
        public async Task<bool> DeployAsync(
            VpsConnection connection,
            List<ServerInstance> instances,
            string pak0Path,
            string cpmaPath,
            string serverExecutablePath,
            string mapsPath = "",
            List<string>? customMaps = null,
            Action<double, string>? progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke(0.0, "Connecting via SSH...");

                var connInfo = _connectionService.GetConnectionInfo(connection);
                using var sshClient = new SshClient(connInfo);
                using var sftpClient = new SftpClient(connInfo);

                sshClient.Connect();
                sftpClient.Connect();

                if (!sshClient.IsConnected)
                {
                    Log("Failed to connect to VPS via SSH", LogLevel.Error);
                    return false;
                }

                progressCallback?.Invoke(5.0, "Checking Docker installation...");
                if (!await EnsureDockerAsync(sshClient))
                {
                    Log("Docker installation failed or is unavailable.", LogLevel.Error);
                    return false;
                }

                // Prepare build context directory
                var remoteContext = "/root/q3docker";
                progressCallback?.Invoke(10.0, "Preparing build context...");

                if (!sftpClient.Exists(remoteContext))
                {
                    sftpClient.CreateDirectory(remoteContext);
                }

                // Create local temp directory for build context
                var localTempDir = Path.Combine(Path.GetTempPath(), "quacke_build_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(localTempDir);

                try
                {
                    // Generate Dockerfile
                    progressCallback?.Invoke(15.0, "Generating Dockerfile...");
                    var dockerfilePath = Path.Combine(localTempDir, "Dockerfile");
                    GenerateDockerfile(dockerfilePath, customMaps != null && customMaps.Any());

                    // Copy server executable
                    progressCallback?.Invoke(20.0, "Copying server executable...");
                    var localExecutable = Path.Combine(localTempDir, "cnq3-server-x64");
                    File.Copy(serverExecutablePath, localExecutable, true);

                    // Copy CPMA folder contents
                    progressCallback?.Invoke(30.0, "Copying CPMA files...");
                    var cpmaDir = Path.Combine(localTempDir, "cpma");
                    CopyDirectory(cpmaPath, cpmaDir, true);

                    // Copy maps folder if provided
                    if (!string.IsNullOrEmpty(mapsPath) && Directory.Exists(mapsPath))
                    {
                        progressCallback?.Invoke(45.0, "Copying community maps...");
                        var mapsDir = Path.Combine(localTempDir, "maps");
                        CopyDirectory(mapsPath, mapsDir, true);
                    }

                    // Copy pak0.pk3
                    progressCallback?.Invoke(55.0, "Copying pak0.pk3...");
                    var localPak0 = Path.Combine(localTempDir, "pak0.pk3");
                    File.Copy(pak0Path, localPak0, true);

                    // Copy custom maps if provided
                    if (customMaps != null && customMaps.Any())
                    {
                        progressCallback?.Invoke(60.0, $"Copying {customMaps.Count} custom maps...");
                        var customMapsDir = Path.Combine(localTempDir, "custom_maps");
                        Directory.CreateDirectory(customMapsDir);

                        foreach (var mapPath in customMaps)
                        {
                            var mapFileName = Path.GetFileName(mapPath);
                            var destPath = Path.Combine(customMapsDir, mapFileName);
                            File.Copy(mapPath, destPath, true);
                        }
                    }

                    // Generate server config for each instance
                    progressCallback?.Invoke(70.0, "Generating server configurations...");
                    var configsDir = Path.Combine(localTempDir, "configs");
                    Directory.CreateDirectory(configsDir);

                    foreach (var instance in instances)
                    {
                        var configPath = Path.Combine(configsDir, $"{instance.Name}.cfg");
                        GenerateServerConfig(instance, configPath);
                    }

                    // Generate docker-compose.yml
                    var composePath = Path.Combine(localTempDir, "docker-compose.yml");
                    GenerateCompose(composePath, instances);

                    // Upload build context to VPS
                    progressCallback?.Invoke(65.0, "Uploading build context...");
                    await UploadBuildContextAsync(sftpClient, localTempDir, remoteContext, progressCallback);

                    // Build Docker image on VPS
                    progressCallback?.Invoke(80.0, "Building Docker image on VPS...");
                    var buildSuccess = await BuildDockerImageAsync(sshClient, remoteContext);
                    if (!buildSuccess)
                    {
                        Log("Failed to build Docker image", LogLevel.Error);
                        return false;
                    }

                    // Start containers with docker-compose
                    progressCallback?.Invoke(90.0, "Starting server containers...");
                    var startSuccess = await StartContainersAsync(sshClient, remoteContext, instances);
                    if (!startSuccess)
                    {
                        Log("Failed to start containers", LogLevel.Error);
                        return false;
                    }

                    progressCallback?.Invoke(100.0, "Deployment complete!");
                    Log("✓ Docker deployment completed successfully", LogLevel.Success);
                    return true;
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        Directory.Delete(localTempDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Docker deployment error: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private async Task<bool> EnsureDockerAsync(SshClient sshClient)
        {
            // Quick check for docker binary
            var check = sshClient.RunCommand("which docker");
            if (check.ExitStatus == 0)
                return true;

            Log("Docker not found. Attempting to install docker & docker-compose...");

            // Basic install for Debian/Ubuntu
            var install = sshClient.RunCommand("apt-get update -y && apt-get install -y docker.io docker-compose");
            if (install.ExitStatus != 0)
            {
                Log($"Docker install failed: {install.Error}", LogLevel.Error);
                return false;
            }

            // Verify again
            var verify = sshClient.RunCommand("which docker");
            return verify.ExitStatus == 0;
        }

        private void GenerateDockerfile(string localPath, bool includeCustomMaps)
        {
            var sb = new StringBuilder();

            // Start from minimal Debian base
            sb.AppendLine("FROM debian:bookworm-slim");
            sb.AppendLine();

            // Install runtime dependencies
            sb.AppendLine("# Install runtime dependencies");
            sb.AppendLine("RUN apt-get update && apt-get install -y \\");
            sb.AppendLine("    ca-certificates \\");
            sb.AppendLine("    libstdc++6 \\");
            sb.AppendLine("    && rm -rf /var/lib/apt/lists/*");
            sb.AppendLine();

            // Create directory structure
            sb.AppendLine("# Create Quake 3 directory structure");
            sb.AppendLine("RUN mkdir -p /opt/q3/baseq3/maps");
            sb.AppendLine();

            // Copy server executable
            sb.AppendLine("# Copy CNQ3 server executable");
            sb.AppendLine("COPY cnq3-server-x64 /opt/q3/cnq3-server-x64");
            sb.AppendLine("RUN chmod +x /opt/q3/cnq3-server-x64");
            sb.AppendLine();

            // Copy CPMA files
            sb.AppendLine("# Copy CPMA mod files");
            sb.AppendLine("COPY cpma/ /opt/q3/cpma/");
            sb.AppendLine();

            // Copy community maps if provided
            sb.AppendLine("# Copy community maps (if provided)");
            sb.AppendLine("COPY maps/*.bsp /opt/q3/baseq3/maps/ 2>/dev/null || true");
            sb.AppendLine();

            // Copy user's legal pak0.pk3
            sb.AppendLine("# Copy user's legal pak0.pk3");
            sb.AppendLine("COPY pak0.pk3 /opt/q3/baseq3/pak0.pk3");
            sb.AppendLine();

            if (includeCustomMaps)
            {
                sb.AppendLine("# Copy custom maps");
                sb.AppendLine("COPY custom_maps/*.bsp /opt/q3/baseq3/maps/");
                sb.AppendLine();
            }

            // Copy server configurations
            sb.AppendLine("# Copy server configurations");
            sb.AppendLine("COPY configs/*.cfg /opt/q3/baseq3/");
            sb.AppendLine();

            // Set working directory
            sb.AppendLine("WORKDIR /opt/q3");
            sb.AppendLine();

            // Expose port
            sb.AppendLine("EXPOSE 27960/udp");
            sb.AppendLine();

            // Set entrypoint
            sb.AppendLine("# Default entrypoint (can be overridden in docker-compose)");
            sb.AppendLine("ENTRYPOINT [\"/opt/q3/cnq3-server-x64\"]");

            File.WriteAllText(localPath, sb.ToString());
        }

        private void GenerateCompose(string localPath, IEnumerable<ServerInstance> instances)
        {
            var sb = new StringBuilder();
            sb.AppendLine("version: '3.8'");
            sb.AppendLine();
            sb.AppendLine("services:");

            foreach (var inst in instances)
            {
                sb.AppendLine($"  {inst.Name}:");
                sb.AppendLine("    build: .");
                sb.AppendLine("    image: quake3-server:latest");
                sb.AppendLine($"    container_name: {inst.Name}");
                sb.AppendLine($"    ports:");
                sb.AppendLine($"      - \"{inst.Port}:27960/udp\"");
                sb.AppendLine($"    command: [\"+set\", \"net_port\", \"27960\", \"+exec\", \"{inst.Name}.cfg\"]");
                sb.AppendLine($"    restart: unless-stopped");
                sb.AppendLine($"    volumes:");
                sb.AppendLine($"      - {inst.Name}_data:/opt/q3/baseq3/demos");
                sb.AppendLine();
            }

            // Define volumes for persistent data
            sb.AppendLine("volumes:");
            foreach (var inst in instances)
            {
                sb.AppendLine($"  {inst.Name}_data:");
            }

            File.WriteAllText(localPath, sb.ToString());
        }

        private void GenerateServerConfig(ServerInstance instance, string localPath)
        {
            var sb = new StringBuilder();

            // Basic server settings
            sb.AppendLine($"set sv_hostname \"{instance.ServerName}\"");
            sb.AppendLine($"set rconpassword \"{instance.RconPassword}\"");
            sb.AppendLine($"set sv_maxclients {instance.MaxClients}");
            sb.AppendLine($"set g_gametype {instance.GameType}");
            sb.AppendLine($"set map {instance.Map}");
            sb.AppendLine();

            // Advanced settings
            foreach (var setting in instance.AdvancedSettings)
            {
                sb.AppendLine($"set {setting.Key} \"{setting.Value}\"");
            }
            sb.AppendLine();

            // Admin and location info
            sb.AppendLine($"set g_adminServer \"{instance.Admin}\"");
            sb.AppendLine($"set sv_location \"{instance.Location}\"");
            sb.AppendLine();

            // Start map
            sb.AppendLine($"map {instance.Map}");

            File.WriteAllText(localPath, sb.ToString());
        }

        private async Task UploadBuildContextAsync(SftpClient sftpClient, string localDir, string remoteDir, Action<double, string>? progressCallback)
        {
            var files = Directory.GetFiles(localDir, "*", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            int uploadedFiles = 0;

            foreach (var localFile in files)
            {
                var relativePath = Path.GetRelativePath(localDir, localFile);
                var remotePath = Path.Combine(remoteDir, relativePath).Replace('\\', '/');

                // Ensure remote directory exists
                var remoteFileDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(remoteFileDir) && !sftpClient.Exists(remoteFileDir))
                {
                    CreateRemoteDirectory(sftpClient, remoteFileDir);
                }

                // Upload file
                using var fileStream = File.OpenRead(localFile);
                sftpClient.UploadFile(fileStream, remotePath, true);

                uploadedFiles++;
                var progress = 65.0 + (uploadedFiles * 15.0 / totalFiles);
                progressCallback?.Invoke(progress, $"Uploading {relativePath}...");
            }
        }

        private void CreateRemoteDirectory(SftpClient sftpClient, string remotePath)
        {
            var parts = remotePath.Split('/');
            var currentPath = string.Empty;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                currentPath += "/" + part;
                if (!sftpClient.Exists(currentPath))
                {
                    sftpClient.CreateDirectory(currentPath);
                }
            }
        }

        private async Task<bool> BuildDockerImageAsync(SshClient sshClient, string remoteContext)
        {
            try
            {
                Log("Building Docker image from Dockerfile...");
                var buildCommand = $"cd {remoteContext} && docker build -t quake3-server:latest .";
                var command = sshClient.CreateCommand(buildCommand);

                var result = await Task.Run(() => command.Execute());
                Log(result);

                if (command.ExitStatus == 0)
                {
                    Log("✓ Docker image built successfully");
                    return true;
                }
                else
                {
                    Log($"✗ Docker build failed: {command.Error}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception during Docker build: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private async Task<bool> StartContainersAsync(SshClient sshClient, string remoteContext, List<ServerInstance> instances)
        {
            try
            {
                // Stop existing containers first
                Log("Stopping existing containers...");
                var stopCommand = $"cd {remoteContext} && docker-compose down";
                var stop = sshClient.RunCommand(stopCommand);
                Log(stop.Result);

                // Start containers with docker-compose
                Log("Starting containers with docker-compose...");
                var startCommand = $"cd {remoteContext} && docker-compose up -d";
                var command = sshClient.CreateCommand(startCommand);

                var result = await Task.Run(() => command.Execute());
                Log(result);

                if (command.ExitStatus == 0)
                {
                    Log($"✓ Successfully started {instances.Count} container(s)");

                    // Update container IDs in instances
                    foreach (var instance in instances)
                    {
                        var containerIdCmd = sshClient.RunCommand($"docker ps -q -f name={instance.Name}");
                        if (containerIdCmd.ExitStatus == 0)
                        {
                            instance.ContainerId = containerIdCmd.Result.Trim();
                        }
                    }

                    return true;
                }
                else
                {
                    Log($"✗ Failed to start containers: {command.Error}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception during container start: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }
}
