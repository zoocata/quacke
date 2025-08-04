using QuakeServerManager.Models;
using QuakeServerManager.ViewModels;
using System.Text;
using System;
using System.IO;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System.Security.Cryptography;

namespace QuakeServerManager.Services
{
    public class SshService
    {
        public event Action<string>? LogMessage;

        private void Log(string message)
        {
            LogMessage?.Invoke(message);
        }

        private string NormalizeLineEndings(string content)
        {
            // Convert Windows line endings (\r\n) and old Mac line endings (\r) to Unix line endings (\n)
            return content.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private string GetFileMd5(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        private string GetDirectoryHash(string directoryPath)
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                               .OrderBy(f => f)
                               .ToList();

            using (var md5 = MD5.Create())
            {
                foreach (var file in files)
                {
                    // Get file path relative to directory
                    var relativePath = Path.GetRelativePath(directoryPath, file);
                    
                    // Add path to hash
                    var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant());
                    md5.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

                    // Add file content to hash
                    var content = File.ReadAllBytes(file);
                    md5.TransformBlock(content, 0, content.Length, content, 0);
                }

                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
            }
        }



        public Task<bool> TestConnectionAsync(VpsConnection connection)
        {
            try
            {
                Log($"Attempting SSH connection to {connection.Ip}:{connection.Port} as {connection.Username}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                
                Log("Connecting to SSH server...");
                client.Connect();
                
                if (client.IsConnected)
                {
                    Log("SSH connection established successfully!");
                    
                    // Test a simple command
                    using var cmd = client.RunCommand("echo 'SSH connection test successful'");
                    Log($"Test command result: {cmd.Result}");
                    
                    client.Disconnect();
                    return Task.FromResult(true);
                }
                else
                {
                    Log("Failed to establish SSH connection.");
                    return Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                Log($"SSH connection failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> SetupVpsEnvironmentAsync(VpsConnection connection, string localQ3Path, Action<double, string>? progressCallback = null)
        {
            try
            {
                Log($"Setting up VPS environment on {connection.Ip}...");
                progressCallback?.Invoke(0.0, "Initializing VPS setup...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for VPS setup.");
                    return false;
                }

                progressCallback?.Invoke(10.0, "Creating base directories...");
                Log("Creating base directories...");
                await CreateBaseDirectoriesAsync(client);

                progressCallback?.Invoke(20.0, "Installing required packages...");
                Log("Installing required packages...");
                await InstallScreenAsync(client);

                progressCallback?.Invoke(30.0, "Uploading Q3 base files...");
                Log("Uploading Q3 base files...");
                await UploadQ3BaseFilesAsync(client, connection, localQ3Path, progressCallback, 30.0, 90.0);

                progressCallback?.Invoke(95.0, "Setting up base permissions...");
                Log("Setting up base permissions...");
                await SetupBasePermissionsAsync(client);

                progressCallback?.Invoke(100.0, "VPS environment setup completed!");
                Log("VPS environment setup completed!");
                client.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                Log($"VPS environment setup failed: {ex.Message}");
                return false;
            }
        }

        private Task CreateBaseDirectoriesAsync(SshClient client)
        {
            var commands = new[]
            {
                "mkdir -p /root/cpma",
                "mkdir -p /root/cpma/baseq3",
                "mkdir -p /root/cpma/classes"
            };

            foreach (var command in commands)
            {
                Log($"Creating directory: {command}");
                using var cmd = client.RunCommand(command);
                if (cmd.ExitStatus != 0)
                {
                    Log($"Warning: Directory creation failed: {cmd.Error}");
                }
            }
            
            return Task.CompletedTask;
        }

        private async Task UploadQ3BaseFilesAsync(SshClient client, VpsConnection connection, string localPath, Action<double, string>? progressCallback = null, double startProgress = 0.0, double endProgress = 100.0)
        {
            try
            {
                Log($"Uploading Q3 base files from: {localPath}");
                
                if (!Directory.Exists(localPath))
                {
                    Log($"Error: Local Q3 path does not exist: {localPath}");
                    return;
                }

                // Handle baseq3 directory separately using SFTP instead of rsync
                var baseq3Path = Path.Combine(localPath, "baseq3");
                if (Directory.Exists(baseq3Path))
                {
                    Log("Syncing baseq3 directory using SFTP...");
                    progressCallback?.Invoke(startProgress + 10, "Syncing baseq3 directory...");
                    
                    // Use SFTP to upload baseq3 directory
                    using var sftpClient = new SftpClient(GetConnectionInfo(connection));
                    sftpClient.Connect();
                    
                    if (sftpClient.IsConnected)
                    {
                        // Ensure remote directory exists
                        try
                        {
                            sftpClient.CreateDirectory("/root/cpma/baseq3");
                        }
                        catch (Exception) { /* Directory might already exist */ }
                        
                        // Upload all files from baseq3 directory
                        var baseq3Files = Directory.GetFiles(baseq3Path, "*", SearchOption.AllDirectories);
                        var totalFiles = baseq3Files.Length;
                        var currentFile = 0;
                        
                        foreach (var file in baseq3Files)
                        {
                            currentFile++;
                            var relativePath = Path.GetRelativePath(baseq3Path, file).Replace('\\', '/');
                            var remotePath = $"/root/cpma/baseq3/{relativePath}";
                            
                            // Ensure remote directory exists
                            var remoteDir = GetUnixDirectoryName(remotePath);
                            if (!string.IsNullOrEmpty(remoteDir))
                            {
                                try
                                {
                                    sftpClient.CreateDirectory(remoteDir);
                                }
                                catch (Exception) { /* Directory might already exist */ }
                            }
                            
                            // Skip if identical file already exists
                            var uploadNeeded = true;
                            if (sftpClient.Exists(remotePath))
                            {
                                var remoteAttr = sftpClient.GetAttributes(remotePath);
                                var localInfo = new FileInfo(file);

                                // simple check: same size ⇒ skip
                                if (remoteAttr.Size == localInfo.Length)
                                {
                                    uploadNeeded = false;
                                    Log($"Skipping existing file: {relativePath} (size: {remoteAttr.Size})");
                                }
                                else
                                {
                                    Log($"File differs, uploading: {relativePath} (remote: size={remoteAttr.Size}; local: size={localInfo.Length})");
                                }
                            }
                            else
                            {
                                Log($"New file, uploading: {relativePath}");
                            }

                            if (uploadNeeded)
                            {
                                // Upload file
                                using var fileStream = File.OpenRead(file);
                                using var remoteStream = sftpClient.Create(remotePath);
                                await fileStream.CopyToAsync(remoteStream);
                                Log($"Uploaded file: {relativePath}");
                            }
                            
                            var progress = startProgress + 10 + ((endProgress - startProgress - 30) * currentFile / totalFiles);
                            progressCallback?.Invoke(progress, $"Uploading baseq3 file: {relativePath}");
                        }
                        
                        sftpClient.Disconnect();
                        Log("Baseq3 directory sync completed via SFTP");
                    }
                    else
                    {
                        Log("Warning: Failed to connect SFTP client for baseq3 sync");
                    }
                }

                // Upload cnq3-server-x64 file individually
                var cnq3Path = Path.Combine(localPath, "cnq3-server-x64");
                if (File.Exists(cnq3Path))
                {
                    Log("Uploading cnq3-server-x64 file...");
                    progressCallback?.Invoke(startProgress + 70, "Uploading cnq3-server-x64...");
                    
                    await UploadItemAsync(client, connection, cnq3Path, "/root/cpma/cnq3-server-x64", progressCallback, startProgress + 70, startProgress + 85);
                }
                else
                {
                    Log($"Warning: cnq3-server-x64 not found in {localPath}");
                }

                progressCallback?.Invoke(endProgress, "Base file transfer completed");
                Log("Base file transfer completed");
                
                // Verify remote directory structure
                Log("Verifying remote directory structure...");
                var verifyCommand = "ls -la /root/cpma/";
                using var verifyCmd = client.RunCommand(verifyCommand);
                Log($"Remote directory contents: {verifyCmd.Result}");
                
                if (!string.IsNullOrEmpty(verifyCmd.Error))
                {
                    Log($"Warning during verification: {verifyCmd.Error}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error during base file upload: {ex.Message}");
            }
        }

        private Task SetupBasePermissionsAsync(SshClient client)
        {
            var commands = new[]
            {
                "chmod +x /root/cpma/cnq3-server-x64",
                "chmod +x /root/cpma/cpma/*.sh"
            };

            foreach (var command in commands)
            {
                Log($"Setting permissions: {command}");
                using var cmd = client.RunCommand(command);
                if (cmd.ExitStatus != 0)
                {
                    Log($"Warning: Permission setup failed: {cmd.Error}");
                }
            }
            
            return Task.CompletedTask;
        }

        public async Task<bool> DeployServerAsync(VpsConnection connection, ServerInstance instance, string localQ3Path, Action<double, string>? progressCallback = null)
        {
            try
            {
                Log($"Starting deployment of instance {instance.Name} to {connection.Ip}...");
                progressCallback?.Invoke(0.0, "Initializing instance deployment...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for deployment.");
                    return false;
                }

                progressCallback?.Invoke(10.0, "Uploading class configuration files...");
                Log("Uploading class configuration files...");
                await UploadClassFilesAsync(client, localQ3Path, instance.Name, progressCallback, 10.0, 40.0);

                progressCallback?.Invoke(45.0, "Generating and uploading server configuration...");
                Log("Generating and uploading server configuration...");
                var configContent = GenerateServerConfig(instance);
                await UploadConfigAsync(client, configContent, instance.Name);

                progressCallback?.Invoke(60.0, "Generating and uploading control scripts...");
                Log("Generating and uploading control scripts...");
                await UploadControlScriptsAsync(client, instance.Name);

                progressCallback?.Invoke(80.0, "Setting up server permissions...");
                Log("Setting up server permissions...");
                await SetupServerAsync(client, instance.Name);

                progressCallback?.Invoke(100.0, "Instance deployment completed successfully!");
                Log("Instance deployment completed successfully!");
                client.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                Log($"Instance deployment failed: {ex.Message}");
                return false;
            }
        }



        public Task<bool> CleanupOrphanedInstancesAsync(VpsConnection connection, List<string> currentInstances)
        {
            try
            {
                Log($"Checking for orphaned instances on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for cleanup.");
                    return Task.FromResult(false);
                }

                // Get list of deployed instances on server
                var deployedInstances = GetDeployedInstancesAsync(client).Result;
                Log($"Found {deployedInstances.Count} deployed instances on server: {string.Join(", ", deployedInstances)}");

                // Find orphaned instances (deployed but not in current list)
                var orphanedInstances = deployedInstances.Except(currentInstances, StringComparer.OrdinalIgnoreCase).ToList();
                
                if (orphanedInstances.Count == 0)
                {
                    Log("No orphaned instances found.");
                    client.Disconnect();
                    return Task.FromResult(true);
                }

                Log($"Found {orphanedInstances.Count} orphaned instances: {string.Join(", ", orphanedInstances)}");

                // Clean up each orphaned instance
                foreach (var orphanedInstance in orphanedInstances)
                {
                    Log($"Cleaning up orphaned instance: {orphanedInstance}");
                    
                    // Stop the server if it's running
                    var stopCommand = $"cd /root/cpma && ./quacke_stop_{orphanedInstance}.sh";
                    using var stopCmd = client.RunCommand(stopCommand);
                    Log($"Stop command for {orphanedInstance}: {stopCmd.Result}");

                    // Remove the instance files
                    var cleanupCommands = new[]
                    {
                        $"rm -f /root/cpma/quacke_start_{orphanedInstance}.sh",
                        $"rm -f /root/cpma/quacke_stop_{orphanedInstance}.sh",
                        $"rm -f /root/cpma/quacke_restart_{orphanedInstance}.sh",
                        $"rm -f /root/cpma/server_{orphanedInstance}.cfg"
                    };

                    foreach (var cleanupCmd in cleanupCommands)
                    {
                        using var cmd = client.RunCommand(cleanupCmd);
                        if (cmd.ExitStatus != 0)
                        {
                            Log($"Warning: Cleanup command failed: {cleanupCmd} - {cmd.Error}");
                        }
                    }

                    Log($"Successfully cleaned up orphaned instance: {orphanedInstance}");
                }

                client.Disconnect();
                Log($"Cleanup completed. Removed {orphanedInstances.Count} orphaned instances.");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log($"Cleanup failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private Task<List<string>> GetDeployedInstancesAsync(SshClient client)
        {
            var deployedInstances = new List<string>();
            
            try
            {
                // Check for start scripts to identify deployed instances
                var command = "cd /root/cpma && ls -1 quacke_start_*.sh 2>/dev/null | sed 's/quacke_start_\\(.*\\)\\.sh/\\1/'";
                using var cmd = client.RunCommand(command);
                
                if (!string.IsNullOrEmpty(cmd.Result))
                {
                    var instances = cmd.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    deployedInstances.AddRange(instances);
                }
                
                Log($"Found deployed instances: {string.Join(", ", deployedInstances)}");
            }
            catch (Exception ex)
            {
                Log($"Error getting deployed instances: {ex.Message}");
            }
            
            return Task.FromResult(deployedInstances);
        }

        public Task<List<string>> GetDeployedInstancesAsync(VpsConnection connection)
        {
            try
            {
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for getting deployed instances.");
                    return Task.FromResult(new List<string>());
                }

                var deployedInstances = GetDeployedInstancesAsync(client).Result;
                client.Disconnect();
                return Task.FromResult(deployedInstances);
            }
            catch (Exception ex)
            {
                Log($"Failed to get deployed instances: {ex.Message}");
                return Task.FromResult(new List<string>());
            }
        }

        public Task<bool> CheckForExistingCpmaInstallationAsync(VpsConnection connection)
        {
            try
            {
                Log($"Checking for existing CPMA installation on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for CPMA check.");
                    return Task.FromResult(false);
                }

                // Check if CPMA directory exists
                var checkCommand = "test -d /root/cpma && echo 'exists'";
                using var cmd = client.RunCommand(checkCommand);
                
                var hasCpma = !string.IsNullOrEmpty(cmd.Result?.Trim());
                
                if (hasCpma)
                {
                    Log("Existing CPMA installation detected!");
                }
                else
                {
                    Log("No existing CPMA installation found.");
                }

                client.Disconnect();
                return Task.FromResult(hasCpma);
            }
            catch (Exception ex)
            {
                Log($"Error checking for CPMA installation: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<List<ServerInstance>> ImportExistingServerConfigurationsAsync(VpsConnection connection)
        {
            var importedInstances = new List<ServerInstance>();
            
            try
            {
                Log($"Importing existing server configurations from {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for configuration import.");
                    return Task.FromResult(importedInstances);
                }

                // Get list of deployed instances
                var deployedInstances = GetDeployedInstancesAsync(client).Result;
                
                // If no instances found via scripts, try to find existing configs directly
                if (deployedInstances.Count == 0)
                {
                    Log("No deployed instances found via scripts, checking for existing configs...");
                    
                    // Find all .cfg files in the cpma directory (no subfolders)
                    var findConfigsCommand = "find /root/cpma/cpma/ -maxdepth 1 -name '*.cfg' -type f 2>/dev/null";
                    using var findConfigsCmd = client.RunCommand(findConfigsCommand);
                    
                    if (!string.IsNullOrEmpty(findConfigsCmd.Result))
                    {
                        var configFiles = findConfigsCmd.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Log($"Found {configFiles.Length} config files in /root/cpma/cpma/");
                        
                        foreach (var configFile in configFiles)
                        {
                            Log($"Checking config file: {configFile}");
                            
                            // Check if this file contains "set sv_hostname"
                            var checkHostnameCommand = $"grep -q 'set sv_hostname' {configFile} && echo 'has_hostname'";
                            using var checkHostnameCmd = client.RunCommand(checkHostnameCommand);
                            
                            if (!string.IsNullOrEmpty(checkHostnameCmd.Result?.Trim()))
                            {
                                Log($"Config file {configFile} contains sv_hostname, will import");
                                
                                // Extract the sv_hostname value to use as instance name
                                // Use a more robust extraction that handles whitespace and comments
                                var extractHostnameCommand = $"grep 'set sv_hostname' {configFile} | head -1 | sed 's/.*set sv_hostname[[:space:]]*\"\\([^\"]*\\)\".*/\\1/'";
                                using var extractHostnameCmd = client.RunCommand(extractHostnameCommand);
                                
                                var instanceName = extractHostnameCmd.Result?.Trim();
                                if (!string.IsNullOrEmpty(instanceName))
                                {
                                    Log($"Using hostname '{instanceName}' as instance name");
                                    deployedInstances.Add(instanceName);
                                }
                                else
                                {
                                    Log($"Could not extract hostname from {configFile}, using filename as instance name");
                                    var fileName = Path.GetFileNameWithoutExtension(configFile);
                                    deployedInstances.Add(fileName);
                                }
                            }
                            else
                            {
                                Log($"Config file {configFile} does not contain sv_hostname, skipping");
                            }
                        }
                    }
                    else
                    {
                        Log("No config files found in /root/cpma/cpma/");
                    }
                }
                
                foreach (var instanceName in deployedInstances)
                {
                    try
                    {
                        Log($"Importing configuration for instance: {instanceName}");
                        
                        // Download server configuration - try multiple possible locations
                        var possibleConfigPaths = new[]
                        {
                            $"/root/cpma/cpma/server.cfg",
                            $"/root/cpma/configs/{instanceName}/server.cfg",
                            $"/root/cpma/server.cfg"
                        };
                        
                        // For existing servers, we need to find the specific config file that contains this instance's hostname
                        if (deployedInstances.Count > 0 && !deployedInstances[0].StartsWith("quacke_"))
                        {
                            // This is an imported instance, find the config file that contains this hostname
                            // Use grep with proper escaping to find the config file containing this hostname
                            var findConfigCommand = $"find /root/cpma/cpma/ -maxdepth 1 -name '*.cfg' -type f -exec grep -l 'set sv_hostname.*\"{instanceName}\"' {{}} \\; 2>/dev/null";
                            using var findConfigCmd = client.RunCommand(findConfigCommand);
                            
                            if (!string.IsNullOrEmpty(findConfigCmd.Result?.Trim()))
                            {
                                var configFile = findConfigCmd.Result.Trim();
                                Log($"Found config file for instance {instanceName}: {configFile}");
                                possibleConfigPaths = new[] { configFile };
                            }
                            else
                            {
                                Log($"Could not find config file for instance {instanceName}");
                                continue;
                            }
                        }
                        
                        string? configContent = null;
                        string? usedPath = null;
                        
                        foreach (var configPath in possibleConfigPaths)
                        {
                            Log($"Checking for config at: {configPath}");
                            var configCommand = $"cat {configPath} 2>/dev/null";
                            using var configCmd = client.RunCommand(configCommand);
                            
                            Log($"Command result length: {configCmd.Result?.Length ?? 0}");
                            if (!string.IsNullOrEmpty(configCmd.Result))
                            {
                                configContent = configCmd.Result;
                                usedPath = configPath;
                                Log($"Found server config at: {usedPath}");
                                break;
                            }
                            else
                            {
                                Log($"No content found at: {configPath}");
                            }
                        }
                        
                        Log($"Raw server config for {instanceName}: '{configContent}'");
                        
                        if (string.IsNullOrEmpty(configContent))
                        {
                            Log($"No configuration found for {instanceName} in any of the expected locations, skipping...");
                            continue;
                        }

                        // Parse server configuration to extract settings
                        var serverConfig = ParseServerConfiguration(configContent);
                        
                        Log($"Parsed server config for {instanceName}: {serverConfig.Count} entries");
                        foreach (var kvp in serverConfig)
                        {
                            Log($"  {kvp.Key} = {kvp.Value}");
                        }
                        
                        // Create ServerInstance from parsed configuration
                        var instance = new ServerInstance
                        {
                            Name = instanceName,
                            ServerName = serverConfig.GetValueOrDefault("sv_hostname", instanceName),
                            Admin = serverConfig.GetValueOrDefault(".admin", "admin"),
                            Location = serverConfig.GetValueOrDefault(".location", "Unknown"),
                            MaxClients = int.TryParse(serverConfig.GetValueOrDefault("sv_maxclients", "24"), out var maxClients) ? maxClients : 24,
                            Port = int.TryParse(serverConfig.GetValueOrDefault("net_port", "27960"), out var port) ? port : 27960,
                            GameType = serverConfig.GetValueOrDefault("g_gametype", "1"),
                            Map = serverConfig.GetValueOrDefault("map", "cpm3a"),
                            RconPassword = serverConfig.GetValueOrDefault("rconPassword", ""),
                            IsDeployed = true
                        };
                        
                        Log($"Server config keys found: {string.Join(", ", serverConfig.Keys)}");
                        Log($"Looking for sv_hostname, found: {serverConfig.GetValueOrDefault("sv_hostname", "NOT_FOUND")}");
                        Log($"Looking for .admin, found: {serverConfig.GetValueOrDefault(".admin", "NOT_FOUND")}");
                        Log($"Looking for .location, found: {serverConfig.GetValueOrDefault(".location", "NOT_FOUND")}");
                        Log($"Looking for sv_maxclients, found: {serverConfig.GetValueOrDefault("sv_maxclients", "NOT_FOUND")}");
                        Log($"Looking for net_port, found: {serverConfig.GetValueOrDefault("net_port", "NOT_FOUND")}");
                        Log($"Looking for g_gametype, found: {serverConfig.GetValueOrDefault("g_gametype", "NOT_FOUND")}");
                        Log($"Looking for map, found: {serverConfig.GetValueOrDefault("map", "NOT_FOUND")}");
                        Log($"Looking for rconPassword, found: {serverConfig.GetValueOrDefault("rconPassword", "NOT_FOUND")}");
                        
                        Log($"Created ServerInstance for {instanceName}:");
                        Log($"  ServerName: {instance.ServerName}");
                        Log($"  Admin: {instance.Admin}");
                        Log($"  Location: {instance.Location}");
                        Log($"  MaxClients: {instance.MaxClients}");
                        Log($"  Port: {instance.Port}");
                        Log($"  GameType: {instance.GameType}");
                        Log($"  Map: {instance.Map}");
                        Log($"  RconPassword: {instance.RconPassword}");

                        // Try to determine the class from the configuration
                        var classConfig = GetClassFromServerConfigAsync(client, instanceName).Result;
                        if (classConfig != null)
                        {
                            instance.SelectedClass = classConfig;
                        }

                        importedInstances.Add(instance);
                        Log($"Successfully imported configuration for {instanceName}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error importing configuration for {instanceName}: {ex.Message}");
                        Log($"Stack trace: {ex.StackTrace}");
                    }
                }

                client.Disconnect();
                Log($"Import completed: {importedInstances.Count} instances imported");
                return Task.FromResult(importedInstances);
            }
            catch (Exception ex)
            {
                Log($"Error importing server configurations: {ex.Message}");
                return Task.FromResult(importedInstances);
            }
        }

        public Task<List<Class>> ImportClassConfigurationsAsync(VpsConnection connection)
        {
            var importedClasses = new List<Class>();
            
            try
            {
                Log($"Importing class configurations from {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log("Failed to establish SSH connection for class configuration import.");
                    return Task.FromResult(importedClasses);
                }

                // Check if cpma/classes directory exists
                var checkClassesDirCommand = "test -d /root/cpma/classes && echo 'exists'";
                using var checkClassesDirCmd = client.RunCommand(checkClassesDirCommand);
                
                if (string.IsNullOrEmpty(checkClassesDirCmd.Result?.Trim()))
                {
                    Log("No cpma/classes directory found on server");
                    client.Disconnect();
                    return Task.FromResult(importedClasses);
                }

                // Find all .cfg files in the cpma/classes directory
                var findClassConfigsCommand = "find /root/cpma/classes/ -name '*.cfg' -type f 2>/dev/null";
                using var findClassConfigsCmd = client.RunCommand(findClassConfigsCommand);
                
                if (string.IsNullOrEmpty(findClassConfigsCmd.Result))
                {
                    Log("No class configuration files found in /root/cpma/classes/");
                    client.Disconnect();
                    return Task.FromResult(importedClasses);
                }

                var classFiles = findClassConfigsCmd.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {classFiles.Length} class configuration files");

                foreach (var classFile in classFiles)
                {
                    try
                    {
                        Log($"Importing class configuration from: {classFile}");
                        
                        // Download the class configuration file
                        var classConfigCommand = $"cat {classFile} 2>/dev/null";
                        using var classConfigCmd = client.RunCommand(classConfigCommand);
                        
                        if (string.IsNullOrEmpty(classConfigCmd.Result))
                        {
                            Log($"No content found in class file: {classFile}");
                            continue;
                        }

                        var classConfigContent = classConfigCmd.Result;
                        Log($"Class config content length: {classConfigContent.Length} characters");

                        // Parse the class configuration
                        var parsedClass = ParseClassConfiguration(classConfigContent, classFile);
                        
                        if (parsedClass != null)
                        {
                            importedClasses.Add(parsedClass);
                            Log($"Successfully imported class: {parsedClass.Name}");
                        }
                        else
                        {
                            Log($"Failed to parse class configuration from: {classFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error importing class configuration from {classFile}: {ex.Message}");
                    }
                }

                client.Disconnect();
                Log($"Class import completed: {importedClasses.Count} classes imported");
                return Task.FromResult(importedClasses);
            }
            catch (Exception ex)
            {
                Log($"Error importing class configurations: {ex.Message}");
                return Task.FromResult(importedClasses);
            }
        }

        private Dictionary<string, string> ParseServerConfiguration(string configContent)
        {
            var config = new Dictionary<string, string>();
            
            try
            {
                Log($"Parsing server configuration content: {configContent.Length} characters");
                var lines = configContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {lines.Length} lines to parse");
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                        continue;
                    
                    Log($"Processing line: '{trimmedLine}'");
                    
                    // Parse Quake config format: "set key value" or "sets .key value"
                    if (trimmedLine.StartsWith("set "))
                    {
                        var parts = trimmedLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var key = parts[1];
                            var value = parts[2];
                            
                            // Extract only the content within quotes, ignoring any trailing comments
                            value = ExtractQuotedValue(value);
                            
                            if (!string.IsNullOrEmpty(value))
                            {
                                config[key] = value;
                                Log($"Parsed: {key} = {value}");
                            }
                        }
                    }
                    else if (trimmedLine.StartsWith("sets "))
                    {
                        var parts = trimmedLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var key = parts[1];
                            var value = parts[2];
                            
                            // Extract only the content within quotes, ignoring any trailing comments
                            value = ExtractQuotedValue(value);
                            
                            if (!string.IsNullOrEmpty(value))
                            {
                                config[key] = value;
                                Log($"Parsed: {key} = {value}");
                            }
                        }
                    }
                }
                
                Log($"Parsed {config.Count} configuration entries");
            }
            catch (Exception ex)
            {
                Log($"Error parsing server configuration: {ex.Message}");
            }
            
            return config;
        }

        private Class? ParseClassConfiguration(string classConfigContent, string classFilePath)
        {
            try
            {
                Log($"Parsing class configuration from: {classFilePath}");
                Log($"Class config content: {classConfigContent.Length} characters");
                
                var lines = classConfigContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {lines.Length} lines to parse");
                
                var classConfig = new Dictionary<string, string>();
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                        continue;
                    
                    Log($"Processing class line: '{trimmedLine}'");
                    
                    // Parse class config format: "key value" (no quotes, no "set" prefix)
                    var parts = trimmedLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var key = parts[0];
                        var value = parts[1];
                        
                        // Remove any trailing comments
                        var commentIndex = value.IndexOf("//");
                        if (commentIndex > 0)
                        {
                            value = value.Substring(0, commentIndex).Trim();
                        }
                        
                        if (!string.IsNullOrEmpty(value))
                        {
                            classConfig[key] = value;
                            Log($"Parsed class config: {key} = {value}");
                        }
                    }
                }
                
                Log($"Parsed {classConfig.Count} class configuration entries");
                
                // Create Class object from parsed configuration
                var className = classConfig.GetValueOrDefault("n", "Unknown");
                var fileName = Path.GetFileName(classFilePath);
                
                var cls = new Class
                {
                    Name = className,
                    Model = classConfig.GetValueOrDefault("m", ""),
                    BaseSpeed = int.TryParse(classConfig.GetValueOrDefault("s", "320"), out var baseSpeed) ? baseSpeed : 320,
                    SpawnHealth = int.TryParse(classConfig.GetValueOrDefault("h", "100"), out var spawnHealth) ? spawnHealth : 100,
                    MaxArmour = int.TryParse(classConfig.GetValueOrDefault("a", "50"), out var maxArmour) ? maxArmour : 50,
                    ArmourClass = int.TryParse(classConfig.GetValueOrDefault("ac", "0"), out var armourClass) ? armourClass : 0,
                    HookType = int.TryParse(classConfig.GetValueOrDefault("ht", "0"), out var hookType) ? hookType : 0,
                    HookPull = int.TryParse(classConfig.GetValueOrDefault("hp", "0"), out var hookPull) ? hookPull : 0,
                    HookSpeed = int.TryParse(classConfig.GetValueOrDefault("hs", "0"), out var hookSpeed) ? hookSpeed : 0,
                    DoubleJump = classConfig.GetValueOrDefault("jd", "0") == "1",
                    RampJump = classConfig.GetValueOrDefault("jr", "0") == "1",
                    Weapon2 = classConfig.GetValueOrDefault("w2", ""),
                    Weapon3 = classConfig.GetValueOrDefault("w3", ""),
                    Weapon4 = classConfig.GetValueOrDefault("w4", ""),
                    Weapon5 = classConfig.GetValueOrDefault("w5", ""),
                    Weapon6 = classConfig.GetValueOrDefault("w6", ""),
                    Weapon7 = classConfig.GetValueOrDefault("w7", ""),
                    Weapon8 = classConfig.GetValueOrDefault("w8", ""),
                    StartingWeapon = int.TryParse(classConfig.GetValueOrDefault("sw", "1"), out var startingWeapon) ? startingWeapon : 1,
                    FileName = fileName
                };
                
                Log($"Created Class object for {className}:");
                Log($"  Model: {cls.Model}");
                Log($"  BaseSpeed: {cls.BaseSpeed}");
                Log($"  SpawnHealth: {cls.SpawnHealth}");
                Log($"  MaxArmour: {cls.MaxArmour}");
                Log($"  ArmourClass: {cls.ArmourClass}");
                Log($"  HookType: {cls.HookType}");
                Log($"  HookPull: {cls.HookPull}");
                Log($"  HookSpeed: {cls.HookSpeed}");
                Log($"  DoubleJump: {cls.DoubleJump}");
                Log($"  RampJump: {cls.RampJump}");
                Log($"  Weapon2: {cls.Weapon2}");
                Log($"  Weapon3: {cls.Weapon3}");
                Log($"  Weapon4: {cls.Weapon4}");
                Log($"  Weapon5: {cls.Weapon5}");
                Log($"  Weapon6: {cls.Weapon6}");
                Log($"  Weapon7: {cls.Weapon7}");
                Log($"  Weapon8: {cls.Weapon8}");
                Log($"  StartingWeapon: {cls.StartingWeapon}");
                Log($"  FileName: {cls.FileName}");
                
                return cls;
            }
            catch (Exception ex)
            {
                Log($"Error parsing class configuration from {classFilePath}: {ex.Message}");
                return null;
            }
        }

        private string ExtractQuotedValue(string input)
        {
            try
            {
                // Find the first quote
                var startQuote = input.IndexOf('"');
                if (startQuote == -1)
                {
                    // No quotes found, return the original value
                    return input;
                }
                
                // Find the closing quote
                var endQuote = input.IndexOf('"', startQuote + 1);
                if (endQuote == -1)
                {
                    // No closing quote found, return the original value
                    return input;
                }
                
                // Extract the content between quotes
                var quotedContent = input.Substring(startQuote + 1, endQuote - startQuote - 1);
                Log($"Extracted quoted value: '{quotedContent}' from '{input}'");
                return quotedContent;
            }
            catch (Exception ex)
            {
                Log($"Error extracting quoted value from '{input}': {ex.Message}");
                return input;
            }
        }

        private Task<Class?> GetClassFromServerConfigAsync(SshClient client, string instanceName)
        {
            try
            {
                Log($"Attempting to determine class for instance: {instanceName}");
                
                // Check for class configuration files
                var classConfigPath = $"/root/cpma/classes/";
                var listCommand = $"ls {classConfigPath}*.cfg 2>/dev/null";
                using var listCmd = client.RunCommand(listCommand);
                
                Log($"Class detection command result: '{listCmd.Result}'");
                
                if (string.IsNullOrEmpty(listCmd.Result))
                {
                    Log($"No class configuration files found for {instanceName}");
                    return Task.FromResult<Class?>(null);
                }
                
                var classFiles = listCmd.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {classFiles.Length} class configuration files");
                
                // Try to find a class configuration that matches our known classes
                foreach (var classFile in classFiles)
                {
                    var fileName = Path.GetFileName(classFile);
                    Log($"Checking class file: {fileName}");
                    
                    // Map common class names to our Class objects
                    if (fileName.Contains("scout", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Matched Scout class for {instanceName}");
                        return Task.FromResult<Class?>(new Class 
                        { 
                            Name = "Scout",
                            Model = "slash",
                            BaseSpeed = 320,
                            SpawnHealth = 100,
                            MaxArmour = 50,
                            ArmourClass = 0,
                            DoubleJump = true,
                            RampJump = true,
                            Weapon3 = "15,25,5,10",
                            Weapon4 = "3,6,1,3",
                            StartingWeapon = 3,
                            FileName = "scout.cfg"
                        });
                    }
                    else if (fileName.Contains("fighter", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Matched Fighter class for {instanceName}");
                        return Task.FromResult<Class?>(new Class 
                        { 
                            Name = "Fighter",
                            Model = "sarge",
                            BaseSpeed = 280,
                            SpawnHealth = 100,
                            MaxArmour = 100,
                            ArmourClass = 1,
                            DoubleJump = true,
                            RampJump = false,
                            Weapon3 = "10,25,5,10",
                            Weapon5 = "5,25,5,10",
                            Weapon8 = "50,100,25,50",
                            StartingWeapon = 3,
                            FileName = "fighter.cfg"
                        });
                    }
                    else if (fileName.Contains("sniper", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Matched Sniper class for {instanceName}");
                        return Task.FromResult<Class?>(new Class 
                        { 
                            Name = "Sniper",
                            Model = "visor",
                            BaseSpeed = 280,
                            SpawnHealth = 100,
                            MaxArmour = 50,
                            ArmourClass = 1,
                            DoubleJump = true,
                            RampJump = false,
                            Weapon2 = "100,100,25,50",
                            Weapon7 = "10,25,5,10",
                            StartingWeapon = 3,
                            FileName = "sniper.cfg"
                        });
                    }
                    else if (fileName.Contains("tank", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Matched Tank class for {instanceName}");
                        return Task.FromResult<Class?>(new Class 
                        { 
                            Name = "Tank",
                            Model = "keel",
                            BaseSpeed = 240,
                            SpawnHealth = 100,
                            MaxArmour = 150,
                            ArmourClass = 2,
                            DoubleJump = false,
                            RampJump = false,
                            Weapon3 = "10,25,5,10",
                            Weapon5 = "10,25,5,10",
                            Weapon6 = "50,150,25,50",
                            StartingWeapon = 3,
                            FileName = "tank.cfg"
                        });
                    }
                }
                
                Log($"No matching class found for {instanceName}");
            }
            catch (Exception ex)
            {
                Log($"Error determining class for {instanceName}: {ex.Message}");
            }
            
            return Task.FromResult<Class?>(null);
        }

        public Task<bool> StartServerAsync(VpsConnection connection, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                Log($"Starting server '{instanceName}' on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var command = $"cd /root/cpma && ./quacke_start_{sanitizedName}.sh";
                Log($"Executing command: {command}");
                
                using var cmd = client.RunCommand(command);
                Log($"Start command output: {cmd.Result}");
                
                if (!string.IsNullOrEmpty(cmd.Error))
                {
                    Log($"Start command error: {cmd.Error}");
                }

                client.Disconnect();
                return Task.FromResult(cmd.ExitStatus == 0);
            }
            catch (Exception ex)
            {
                Log($"Failed to start server: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> StopServerAsync(VpsConnection connection, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                Log($"Stopping server '{instanceName}' on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var command = $"cd /root/cpma && ./quacke_stop_{sanitizedName}.sh";
                Log($"Executing command: {command}");
                
                using var cmd = client.RunCommand(command);
                Log($"Stop command output: {cmd.Result}");
                
                if (!string.IsNullOrEmpty(cmd.Error))
                {
                    Log($"Stop command error: {cmd.Error}");
                }

                client.Disconnect();
                return Task.FromResult(cmd.ExitStatus == 0);
            }
            catch (Exception ex)
            {
                Log($"Failed to stop server: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> RestartServerAsync(VpsConnection connection, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                Log($"Restarting server '{instanceName}' on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var command = $"cd /root/cpma && ./quacke_restart_{sanitizedName}.sh";
                Log($"Executing command: {command}");
                
                using var cmd = client.RunCommand(command);
                Log($"Restart command output: {cmd.Result}");
                
                if (!string.IsNullOrEmpty(cmd.Error))
                {
                    Log($"Restart command error: {cmd.Error}");
                }

                client.Disconnect();
                return Task.FromResult(cmd.ExitStatus == 0);
            }
            catch (Exception ex)
            {
                Log($"Failed to restart server: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> CheckServerStatusAsync(VpsConnection connection, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                Log($"Checking status of server '{instanceName}' on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var command = $"screen -list | grep q{sanitizedName}";
                Log($"Executing command: {command}");
                
                using var cmd = client.RunCommand(command);
                Log($"Status check output: {cmd.Result}");

                client.Disconnect();
                return Task.FromResult(!string.IsNullOrEmpty(cmd.Result));
            }
            catch (Exception ex)
            {
                Log($"Failed to check server status: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private ConnectionInfo GetConnectionInfo(VpsConnection connection)
        {
            if (connection.AuthMethod == AuthMethod.Password)
            {
                Log("Using password authentication");
                return new PasswordConnectionInfo(connection.Ip, connection.Username, connection.Password);
            }
            else
            {
                Log($"Using private key authentication with file: {connection.PrivateKeyPath}");
                return new PrivateKeyConnectionInfo(connection.Ip, connection.Username, new PrivateKeyFile(connection.PrivateKeyPath));
            }
        }

        private Task CreateRemoteDirectoriesAsync(SshClient client, string instanceName)
        {
            var commands = new[]
            {
                "mkdir -p /root/cpma",
                "mkdir -p /root/cpma/baseq3",
                "mkdir -p /root/cpma/classes"
            };

            foreach (var command in commands)
            {
                Log($"Creating directory: {command}");
                using var cmd = client.RunCommand(command);
                if (cmd.ExitStatus != 0)
                {
                    Log($"Warning: Directory creation failed: {cmd.Error}");
                }
            }
            
            return Task.CompletedTask;
        }

        private Task InstallScreenAsync(SshClient client)
        {
            try
            {
                Log("Checking if screen and rsync are already installed...");
                
                // First check if screen and rsync are already installed
                using var screenCheckCmd = client.RunCommand("which screen");
                using var rsyncCheckCmd = client.RunCommand("which rsync");
                Log($"Screen check result: {screenCheckCmd.Result}");
                Log($"Rsync check result: {rsyncCheckCmd.Result}");
                
                bool screenInstalled = screenCheckCmd.ExitStatus == 0;
                bool rsyncInstalled = rsyncCheckCmd.ExitStatus == 0;
                
                if (screenInstalled && rsyncInstalled)
                {
                    Log("Screen and rsync are already installed");
                    return Task.CompletedTask;
                }

                Log($"Screen installed: {screenInstalled}, Rsync installed: {rsyncInstalled}");
                Log("Installing missing packages...");
                
                // Check what package manager is available
                using var aptCheck = client.RunCommand("which apt");
                using var yumCheck = client.RunCommand("which yum");
                using var dnfCheck = client.RunCommand("which dnf");
                
                Log($"Package managers found - apt: {aptCheck.ExitStatus == 0}, yum: {yumCheck.ExitStatus == 0}, dnf: {dnfCheck.ExitStatus == 0}");
                
                bool packagesInstalled = false;
                
                // Try apt first (Debian/Ubuntu)
                if (aptCheck.ExitStatus == 0)
                {
                    Log("Using apt package manager...");
                    var packagesToInstall = new List<string>();
                    if (!screenInstalled) packagesToInstall.Add("screen");
                    if (!rsyncInstalled) packagesToInstall.Add("rsync");
                    
                    if (packagesToInstall.Count > 0)
                    {
                        var aptCommands = new List<string>
                        {
                            "apt update -y"
                        };
                        
                        // Install all missing packages in one command
                        aptCommands.Add($"apt install -y {string.Join(" ", packagesToInstall)}");

                        foreach (var command in aptCommands)
                        {
                            Log($"Executing: {command}");
                            using var cmd = client.RunCommand(command);
                            Log($"Command output: {cmd.Result}");
                            Log($"Command error: {cmd.Error}");
                            Log($"Exit status: {cmd.ExitStatus}");
                            
                            if (cmd.ExitStatus != 0 && command.Contains("install"))
                            {
                                Log($"Apt install failed, trying alternative...");
                                break;
                            }
                        }
                        
                        // Verify apt installation
                        using var screenVerify = client.RunCommand("which screen");
                        using var rsyncVerify = client.RunCommand("which rsync");
                        if (screenVerify.ExitStatus == 0 && rsyncVerify.ExitStatus == 0)
                        {
                            Log("Screen and rsync installed successfully using apt");
                            packagesInstalled = true;
                        }
                    }
                }
                
                // Try yum if apt failed or not available
                if (!packagesInstalled && yumCheck.ExitStatus == 0)
                {
                    Log("Using yum package manager...");
                    var packagesToInstall = new List<string>();
                    if (!screenInstalled) packagesToInstall.Add("screen");
                    if (!rsyncInstalled) packagesToInstall.Add("rsync");
                    
                    if (packagesToInstall.Count > 0)
                    {
                        using var yumCmd = client.RunCommand($"yum install -y {string.Join(" ", packagesToInstall)}");
                        Log($"Yum output: {yumCmd.Result}");
                        Log($"Yum error: {yumCmd.Error}");
                        Log($"Yum exit status: {yumCmd.ExitStatus}");
                        
                        if (yumCmd.ExitStatus == 0)
                        {
                            Log("Screen and rsync installed successfully using yum");
                            packagesInstalled = true;
                        }
                    }
                }
                
                // Try dnf if yum failed or not available
                if (!packagesInstalled && dnfCheck.ExitStatus == 0)
                {
                    Log("Using dnf package manager...");
                    var packagesToInstall = new List<string>();
                    if (!screenInstalled) packagesToInstall.Add("screen");
                    if (!rsyncInstalled) packagesToInstall.Add("rsync");
                    
                    if (packagesToInstall.Count > 0)
                    {
                        using var dnfCmd = client.RunCommand($"dnf install -y {string.Join(" ", packagesToInstall)}");
                        Log($"Dnf output: {dnfCmd.Result}");
                        Log($"Dnf error: {dnfCmd.Error}");
                        Log($"Dnf exit status: {dnfCmd.ExitStatus}");
                        
                        if (dnfCmd.ExitStatus == 0)
                        {
                            Log("Screen and rsync installed successfully using dnf");
                            packagesInstalled = true;
                        }
                    }
                }
                
                // Final verification
                using var screenFinalVerify = client.RunCommand("which screen");
                using var rsyncFinalVerify = client.RunCommand("which rsync");
                Log($"Final verification - which screen result: {screenFinalVerify.Result}");
                Log($"Final verification - which rsync result: {rsyncFinalVerify.Result}");
                Log($"Final verification - screen exit status: {screenFinalVerify.ExitStatus}");
                Log($"Final verification - rsync exit status: {rsyncFinalVerify.ExitStatus}");
                
                if (screenFinalVerify.ExitStatus == 0 && rsyncFinalVerify.ExitStatus == 0)
                {
                    Log("Screen and rsync installation verified successfully");
                    
                    // Test screen functionality
                    using var testCmd = client.RunCommand("screen -version");
                    Log($"Screen version test: {testCmd.Result}");
                    
                    // Test rsync functionality
                    using var rsyncTestCmd = client.RunCommand("rsync --version");
                    Log($"Rsync version test: {rsyncTestCmd.Result}");
                }
                else
                {
                    Log("ERROR: Screen or rsync installation failed. Manual installation required.");
                    Log("Attempting manual installation with alternative methods...");
                    
                    // Try alternative installation methods
                    var alternativeCommands = new[]
                    {
                        "curl -sSL https://get.screen.sh | sh",
                        "wget -O - https://get.screen.sh | sh",
                        "yum install -y screen rsync --nogpgcheck",
                        "apt-get install -y screen rsync --force-yes"
                    };
                    
                    foreach (var altCmd in alternativeCommands)
                    {
                        Log($"Trying alternative: {altCmd}");
                        using var altCmdResult = client.RunCommand(altCmd);
                        Log($"Alternative command result: {altCmdResult.Result}");
                        Log($"Alternative command error: {altCmdResult.Error}");
                        
                        // Check if both screen and rsync are now available
                        using var screenFinalCheck = client.RunCommand("which screen");
                        using var rsyncFinalCheck = client.RunCommand("which rsync");
                        if (screenFinalCheck.ExitStatus == 0 && rsyncFinalCheck.ExitStatus == 0)
                        {
                            Log("Screen and rsync installed successfully using alternative method");
                            return Task.CompletedTask;
                        }
                    }
                    
                    throw new Exception("Screen or rsync installation failed - these are required for server management. Please install manually: 'apt install screen rsync' or 'yum install screen rsync'");
                }
            }
            catch (Exception ex)
            {
                Log($"Error installing screen and rsync: {ex.Message}");
                throw; // Re-throw to stop deployment if installation fails
            }
            
            return Task.CompletedTask;
        }













        private Task UploadClassFilesAsync(SshClient client, string localPath, string instanceName, Action<double, string>? progressCallback = null, double startProgress = 0.0, double endProgress = 100.0)
        {
            try
            {
                Log("Uploading class configuration files...");
                progressCallback?.Invoke(startProgress, "Preparing to upload class configuration files...");
                
                // Get the main view model to access available classes
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                var mainViewModel = mainWindow?.DataContext as MainViewModel;
                
                if (mainViewModel?.AvailableClasses == null)
                {
                    Log("Warning: No class configurations available");
                    return Task.CompletedTask;
                }

                Log($"Found {mainViewModel.AvailableClasses.Count} class configurations");

                var uploadedCount = 0;
                // Upload all available class configurations
                foreach (var classConfig in mainViewModel.AvailableClasses)
                {
                    var fileName = classConfig.FileName;
                    var progress = startProgress + (endProgress - startProgress) * uploadedCount / mainViewModel.AvailableClasses.Count;
                    progressCallback?.Invoke(progress, $"Uploading class file: {fileName}");
                    Log($"Uploading class file {uploadedCount + 1}/{mainViewModel.AvailableClasses.Count}: {fileName}");
                    
                    // Generate the class configuration content and normalize line endings
                    var classContent = GenerateClassConfig(classConfig);
                    var normalizedClassContent = NormalizeLineEndings(classContent);
                    
                    // Escape the content for shell command
                    var escapedContent = normalizedClassContent.Replace("'", "'\"'\"'");
                    var command = $"echo '{escapedContent}' > /root/cpma/classes/{fileName}";
                    
                    using var cmd = client.RunCommand(command);
                    if (cmd.ExitStatus == 0)
                    {
                        Log($"Class file {fileName} uploaded successfully");
                    }
                    else
                    {
                        Log($"Error uploading class file {fileName}: {cmd.Error}");
                    }
                    uploadedCount++;
                }
                
                progressCallback?.Invoke(endProgress, "Class configuration files upload completed");
                Log($"Successfully uploaded {uploadedCount} class configuration files");
            }
            catch (Exception ex)
            {
                Log($"Error uploading class files: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private Task UploadConfigAsync(SshClient client, string configContent, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                var configPath = $"/root/cpma/server_{sanitizedName}.cfg";
                Log($"Uploading server configuration to: {configPath}");
                
                // Use SFTP to upload the config content directly
                using var sftpClient = new SftpClient(client.ConnectionInfo);
                sftpClient.Connect();
                
                if (sftpClient.IsConnected)
                {
                    try
                    {
                        // Normalize line endings to Unix format and convert to bytes using UTF-8 encoding
                        var normalizedConfig = NormalizeLineEndings(configContent);
                        var configBytes = Encoding.UTF8.GetBytes(normalizedConfig);
                        
                        // Upload the config content directly
                        using var remoteStream = sftpClient.Create(configPath);
                        remoteStream.Write(configBytes, 0, configBytes.Length);
                        remoteStream.Flush();
                        
                        Log("Server configuration uploaded successfully");
                        
                        // Verify the file was created and has content
                        var verifyCommand = $"ls -la {configPath} && echo '--- Content ---' && head -5 {configPath}";
                        using var verifyCmd = client.RunCommand(verifyCommand);
                        Log($"Config verification: {verifyCmd.Result}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error uploading configuration: {ex.Message}");
                    }
                    finally
                    {
                        sftpClient.Disconnect();
                    }
                }
                else
                {
                    Log("Failed to connect SFTP client for config upload");
                }
            }
            catch (Exception ex)
            {
                Log($"Error uploading configuration: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private Task UploadControlScriptsAsync(SshClient client, string instanceName)
        {
            try
            {
                var scripts = GenerateControlScripts(instanceName);
                Log($"Generating {scripts.Count} control scripts for instance: {instanceName}");
                
                var uploadedCount = 0;
                foreach (var script in scripts)
                {
                    Log($"Uploading script {uploadedCount + 1}/{scripts.Count}: {script.Key}");
                    
                    // Use SFTP to upload the script content directly
                    using var sftpClient = new SftpClient(client.ConnectionInfo);
                    sftpClient.Connect();
                    
                    if (sftpClient.IsConnected)
                    {
                        try
                        {
                            var remotePath = $"/root/cpma/{script.Key}";
                            
                            // Normalize line endings to Unix format and convert to bytes using UTF-8 encoding
                            var normalizedScript = NormalizeLineEndings(script.Value);
                            var scriptBytes = Encoding.UTF8.GetBytes(normalizedScript);
                            
                            // Upload the script content directly
                            using var remoteStream = sftpClient.Create(remotePath);
                            remoteStream.Write(scriptBytes, 0, scriptBytes.Length);
                            remoteStream.Flush();
                            
                            // Make the script executable
                            var chmodCommand = $"chmod +x /root/cpma/{script.Key}";
                            using var chmodCmd = client.RunCommand(chmodCommand);
                            if (chmodCmd.ExitStatus == 0)
                            {
                                Log($"Script {script.Key} uploaded and made executable successfully");
                                
                                // Verify the file was created and has content
                                var verifyCommand = $"ls -la /root/cpma/{script.Key} && echo '--- Content ---' && head -5 /root/cpma/{script.Key}";
                                using var verifyCmd = client.RunCommand(verifyCommand);
                                Log($"Script verification: {verifyCmd.Result}");
                            }
                            else
                            {
                                Log($"Error making script {script.Key} executable: {chmodCmd.Error}");
                            }
                            
                            uploadedCount++;
                        }
                        catch (Exception ex)
                        {
                            Log($"Error uploading script {script.Key}: {ex.Message}");
                        }
                        finally
                        {
                            sftpClient.Disconnect();
                        }
                    }
                    else
                    {
                        Log($"Failed to connect SFTP client for script {script.Key}");
                    }
                }
                
                Log($"Successfully uploaded {uploadedCount} control scripts for instance: {instanceName}");
            }
            catch (Exception ex)
            {
                Log($"Error uploading control scripts: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private Task SetupServerAsync(SshClient client, string instanceName)
        {
            try
            {
                var sanitizedName = SanitizeInstanceName(instanceName);
                Log("Setting up server permissions...");
                
                // List all files in the directory to verify what's there
                var listCommand = "cd /root/cpma && ls -la";
                using var listCmd = client.RunCommand(listCommand);
                Log($"Files in /root/cpma: {listCmd.Result}");
                
                // Set permissions for specific script files
                var scriptFiles = new[]
                {
                    $"quacke_start_{sanitizedName}.sh",
                    $"quacke_stop_{sanitizedName}.sh",
                    $"quacke_restart_{sanitizedName}.sh"
                };
                
                foreach (var scriptFile in scriptFiles)
                {
                    var chmodCommand = $"chmod +x /root/cpma/{scriptFile}";
                    Log($"Executing: {chmodCommand}");
                    using var cmd = client.RunCommand(chmodCommand);
                    if (cmd.ExitStatus == 0)
                    {
                        Log($"Successfully made {scriptFile} executable");
                    }
                    else
                    {
                        Log($"Warning: Failed to make {scriptFile} executable: {cmd.Error}");
                    }
                }
                
                // Make server binary executable if it exists
                var serverBinaryCommand = "chmod +x /root/cpma/cnq3-server-x64";
                using var serverCmd = client.RunCommand(serverBinaryCommand);
                if (serverCmd.ExitStatus == 0)
                {
                    Log("Successfully made server binary executable");
                }
                else
                {
                    Log($"Warning: Failed to make server binary executable: {serverCmd.Error}");
                }
                
                Log("Server setup completed");
            }
            catch (Exception ex)
            {
                Log($"Error setting up server: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private string GenerateServerConfig(ServerInstance instance)
        {
            var config = new StringBuilder();
            
            // Basic settings
            config.AppendLine($"sets .admin \"{instance.Admin}\"");
            config.AppendLine($"sets .location \"{instance.Location}\"");
            config.AppendLine($"set sv_hostname \"{instance.ServerName}\"");
            config.AppendLine($"set rconPassword \"{instance.RconPassword}\"");
            config.AppendLine($"set sv_maxclients {instance.MaxClients}");
            config.AppendLine($"set net_port {instance.Port}");
            config.AppendLine($"set g_gametype {instance.GameType}");
            
            // Advanced settings from the dictionary
            foreach (var setting in instance.AdvancedSettings)
            {
                config.AppendLine($"set {setting.Key} {setting.Value}");
            }

            // Add class configuration if a class is selected
            if (instance.SelectedClass != null)
            {
                config.AppendLine($"// Class configuration");
                config.AppendLine($"exec classes/{instance.SelectedClass.FileName}");
            }

            return config.ToString();
        }

        private string GenerateClassConfig(Class classConfig)
        {
            var config = new StringBuilder();
            // n: Name
            config.AppendLine($"n {classConfig.Name}");
            // m: Model
            if (!string.IsNullOrEmpty(classConfig.Model))
                config.AppendLine($"m {classConfig.Model}");
            // s: Base Speed
            config.AppendLine($"s {classConfig.BaseSpeed}");
            // h: Spawn Health
            config.AppendLine($"h {classConfig.SpawnHealth}");
            // a: Maximum Armour
            config.AppendLine($"a {classConfig.MaxArmour}");
            // ac: Armour Class
            config.AppendLine($"ac {classConfig.ArmourClass}");
            // ht: Hook Type
            if (classConfig.HookType != 0)
                config.AppendLine($"ht {classConfig.HookType}");
            // hp: Hook Pull
            if (classConfig.HookPull != 0)
                config.AppendLine($"hp {classConfig.HookPull}");
            // hs: Hook Speed
            if (classConfig.HookSpeed != 0)
                config.AppendLine($"hs {classConfig.HookSpeed}");
            // jd: Double Jump
            if (classConfig.DoubleJump)
                config.AppendLine("jd 1");
            // jr: Ramp Jump
            if (classConfig.RampJump)
                config.AppendLine("jr 1");
            // w2-w8: Weapon loadouts
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon2))
                config.AppendLine($"w2 {classConfig.Weapon2}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon3))
                config.AppendLine($"w3 {classConfig.Weapon3}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon4))
                config.AppendLine($"w4 {classConfig.Weapon4}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon5))
                config.AppendLine($"w5 {classConfig.Weapon5}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon6))
                config.AppendLine($"w6 {classConfig.Weapon6}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon7))
                config.AppendLine($"w7 {classConfig.Weapon7}");
            if (!string.IsNullOrWhiteSpace(classConfig.Weapon8))
                config.AppendLine($"w8 {classConfig.Weapon8}");
            // sw: Starting Weapon
            config.AppendLine($"sw {classConfig.StartingWeapon}");
            return config.ToString();
        }

        private string SanitizeInstanceName(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                throw new ArgumentException("Instance name cannot be empty.");
            }

            // Allow ONLY a-z, A-Z, 0-9, underscore and hyphen.
            var sanitized = new string(instanceName.Where(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new ArgumentException("Instance name contained no valid characters. Allowed: a-z, A-Z, 0-9, _, -");
            }

            return sanitized;
        }

        private string GetUnixDirectoryName(string unixPath)
        {
            // Handle Unix-style paths correctly, even on Windows
            var normalizedPath = unixPath.Replace('\\', '/');
            var lastSlashIndex = normalizedPath.LastIndexOf('/');
            if (lastSlashIndex <= 0)
                return "/";
            return normalizedPath.Substring(0, lastSlashIndex);
        }

        private Dictionary<string, string> GenerateControlScripts(string instanceName)
        {
            var sanitizedName = SanitizeInstanceName(instanceName);
            var screenName = $"q{sanitizedName}";
            var cfgFile = $"server_{sanitizedName}.cfg";

            return new Dictionary<string, string>
            {
                { $"quacke_start_{sanitizedName}.sh", $@"#!/bin/sh
cd /root/cpma
screen -S {screenName} -X select . > /dev/null 2>&1
if [ $? -eq 1 ]; then
    screen -A -m -d -S {screenName} \
        ./cnq3-server-x64 \
        +set dedicated 2 \
        +set sv_master1 master.quake3arena.com:27950 \
        +set sv_master2 master.ioquake3.org:27950 \
        +set sv_master3 master.maverickservers.com:27950 \
        +set sv_master4 master.quakeservers.net:27950 \
        +set sv_master5 master.qtracker.com:27900 \
        +set fs_game cpma \
        +set ttycon 1 \
        +set developer 0 \
        +exec {cfgFile} \
        +map cpm3a
fi" },
                { $"quacke_stop_{sanitizedName}.sh", $@"#!/bin/sh
cd /root/cpma
screen -S {screenName} -X quit" },
                { $"quacke_restart_{sanitizedName}.sh", $@"#!/bin/sh
cd /root/cpma
./quacke_stop_{sanitizedName}.sh
sleep 1
./quacke_start_{sanitizedName}.sh" }
            };
        }
        
        // Master Control Methods
        public async Task<bool> SyncMapsAsync(VpsConnection connection, string q3Path)
        {
            try
            {
                Log($"Syncing maps to {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var localMapsPath = Path.Combine(q3Path, "baseq3");
                if (!Directory.Exists(localMapsPath))
                {
                    Log($"Local maps folder not found: {localMapsPath}");
                    return false;
                }

                // Use SFTP instead of rsync for map sync
                using var sftpClient = new SftpClient(info);
                sftpClient.Connect();
                
                if (!sftpClient.IsConnected)
                {
                    Log("Failed to connect SFTP client for map sync");
                    return false;
                }

                // Ensure remote directory exists
                try
                {
                    sftpClient.CreateDirectory("/root/cpma/baseq3");
                }
                catch (Exception) { /* Directory might already exist */ }

                // Upload only new or modified map files
                var mapFiles = Directory.GetFiles(localMapsPath, "*", SearchOption.AllDirectories);
                var totalFiles = mapFiles.Length;
                var currentFile = 0;
                var uploadedFiles = 0;
                
                Log($"Found {totalFiles} files to sync");
                
                foreach (var file in mapFiles)
                {
                    currentFile++;
                    var relativePath = Path.GetRelativePath(localMapsPath, file).Replace('\\', '/');
                    var remotePath = $"/root/cpma/baseq3/{relativePath}";
                    
                    // Ensure remote directory exists
                    var remoteDir = GetUnixDirectoryName(remotePath);
                    if (!string.IsNullOrEmpty(remoteDir))
                    {
                        try
                        {
                            if (!sftpClient.Exists(remoteDir))
                                sftpClient.CreateDirectory(remoteDir);
                        }
                        catch (Exception) { /* Directory might already exist */ }
                    }
                    
                    // Skip if identical file already exists
                    var uploadNeeded = true;
                    if (sftpClient.Exists(remotePath))
                    {
                        var remoteAttr = sftpClient.GetAttributes(remotePath);
                        var localInfo = new FileInfo(file);

                        // simple check: same size ⇒ skip
                        if (remoteAttr.Size == localInfo.Length)
                        {
                            uploadNeeded = false;
                        }
                    }

                    if (!uploadNeeded) continue;

                    // Upload file
                    using var fileStream = File.OpenRead(file);
                    using var remoteStream = sftpClient.Create(remotePath);
                    await fileStream.CopyToAsync(remoteStream);
                    uploadedFiles++;
                    
                    if (currentFile % 500 == 0 || currentFile == totalFiles)
                    {
                        Log($"Map sync progress: {currentFile}/{totalFiles} files processed, {uploadedFiles} uploaded");
                    }
                }
                
                Log($"Map sync completed: {uploadedFiles} files uploaded out of {totalFiles} total files");

                sftpClient.Disconnect();
                client.Disconnect();
                Log($"Map sync completed for {connection.Ip}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error syncing maps to {connection.Ip}: {ex.Message}");
                return false;
            }
        }
        
        public Task<bool> RestartAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            try
            {
                Log($"Restarting all servers on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var success = true;
                foreach (var instanceName in instanceNames)
                {
                    var sanitizedName = SanitizeInstanceName(instanceName);
                    Log($"Restarting server instance '{instanceName}'...");
                    var command = $"cd /root/cpma && ./quacke_stop_{sanitizedName}.sh && sleep 1 && ./quacke_start_{sanitizedName}.sh";
                    Log($"Executing command: {command}");
                    
                    using var cmd = client.RunCommand(command);
                    Log($"Restart command output for {instanceName}: {cmd.Result}");
                    
                    if (!string.IsNullOrEmpty(cmd.Error))
                    {
                        Log($"Restart command error for {instanceName}: {cmd.Error}");
                        success = false;
                    }
                    
                    if (cmd.ExitStatus != 0)
                    {
                        Log($"Restart command failed for {instanceName} with exit status: {cmd.ExitStatus}");
                        success = false;
                    }
                }

                client.Disconnect();
                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Log($"Failed to restart all servers on {connection.Ip}: {ex.Message}");
                return Task.FromResult(false);
            }
        }
        
        public Task<bool> StopAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            try
            {
                Log($"Stopping all servers on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var success = true;
                foreach (var instanceName in instanceNames)
                {
                    var sanitizedName = SanitizeInstanceName(instanceName);
                    Log($"Stopping server instance '{instanceName}'...");
                    var command = $"cd /root/cpma && ./quacke_stop_{sanitizedName}.sh";
                    Log($"Executing command: {command}");
                    
                    using var cmd = client.RunCommand(command);
                    Log($"Stop command output for {instanceName}: {cmd.Result}");
                    
                    if (!string.IsNullOrEmpty(cmd.Error))
                    {
                        Log($"Stop command error for {instanceName}: {cmd.Error}");
                        success = false;
                    }
                    
                    if (cmd.ExitStatus != 0)
                    {
                        Log($"Stop command failed for {instanceName} with exit status: {cmd.ExitStatus}");
                        success = false;
                    }
                }

                client.Disconnect();
                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Log($"Failed to stop all servers on {connection.Ip}: {ex.Message}");
                return Task.FromResult(false);
            }
        }
        
        public Task<bool> StartAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            try
            {
                Log($"Starting all servers on {connection.Ip}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                var success = true;
                foreach (var instanceName in instanceNames)
                {
                    var sanitizedName = SanitizeInstanceName(instanceName);
                    Log($"Starting server instance '{instanceName}'...");
                    var command = $"cd /root/cpma && ./quacke_start_{sanitizedName}.sh";
                    Log($"Executing command: {command}");
                    
                    using var cmd = client.RunCommand(command);
                    Log($"Start command output for {instanceName}: {cmd.Result}");
                    
                    if (!string.IsNullOrEmpty(cmd.Error))
                    {
                        Log($"Start command error for {instanceName}: {cmd.Error}");
                        success = false;
                    }
                    
                    if (cmd.ExitStatus != 0)
                    {
                        Log($"Start command failed for {instanceName} with exit status: {cmd.ExitStatus}");
                        success = false;
                    }
                }

                client.Disconnect();
                return Task.FromResult(success);
            }
            catch (Exception ex)
            {
                Log($"Failed to start all servers on {connection.Ip}: {ex.Message}");
                return Task.FromResult(false);
            }
        }







        private async Task UploadQ3FilesAsync(SshClient client, VpsConnection connection, string localPath, string instanceName, Action<double, string>? progressCallback = null, double startProgress = 0.0, double endProgress = 100.0)
        {
            try
            {
                Log($"Uploading Q3 files from: {localPath}");
                progressCallback?.Invoke(startProgress, "Preparing file transfer...");
                
                if (!Directory.Exists(localPath))
                {
                    Log($"Error: Local Q3 path does not exist: {localPath}");
                        return;
                }

                // Handle baseq3 directory separately using rsync
                var baseq3Path = Path.Combine(localPath, "baseq3");
                if (Directory.Exists(baseq3Path))
                {
                    Log("Syncing baseq3 directory using rsync...");
                    progressCallback?.Invoke(startProgress + 10, "Syncing baseq3 directory...");
                    
                    var rsyncProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "rsync",
                            Arguments = $"-avz --delete \"{baseq3Path}/\" -e \"ssh -p {connection.Port} -i \\\"{connection.PrivateKeyPath}\\\" -o StrictHostKeyChecking=no\" {connection.Username}@{connection.Ip}:/root/cpma/baseq3/",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    
                    rsyncProcess.Start();
                    await rsyncProcess.WaitForExitAsync();
                    
                    if (rsyncProcess.ExitCode != 0)
                    {
                        var error = await rsyncProcess.StandardError.ReadToEndAsync();
                        Log($"Warning: rsync for baseq3 returned non-zero exit code: {error}");
                    }
                }

                // Upload other essential files using tar
                var essentialItems = new[] { "cnq3-server-x64", "cpma" };
                var totalItems = essentialItems.Length;
                var currentItem = 0;

                // Upload essential items
                foreach (var item in essentialItems)
                {
                    var localItem = Path.Combine(localPath, item);
                    var remoteItem = $"/root/cpma/{item}";
                    
                    if (File.Exists(localItem) || Directory.Exists(localItem))
                    {
                        currentItem++;
                        var itemProgress = startProgress + 20 + ((endProgress - startProgress - 20) * (currentItem - 1) / totalItems);
                        var nextItemProgress = startProgress + 20 + ((endProgress - startProgress - 20) * currentItem / totalItems);
                        
                        progressCallback?.Invoke(itemProgress, $"Uploading: {item}");
                        Log($"Uploading: {item}");
                        
                        await UploadItemAsync(client, connection, localItem, remoteItem, progressCallback, itemProgress, nextItemProgress);
                    }
                    else
                    {
                        Log($"Warning: {item} not found in {localPath}");
                    }
                }

                progressCallback?.Invoke(endProgress, "File transfer completed");
                Log("File transfer completed");
                
                // Verify remote directory structure
                Log("Verifying remote directory structure...");
                var verifyCommand = "ls -la /root/cpma/";
                using var verifyCmd = client.RunCommand(verifyCommand);
                Log($"Remote directory contents: {verifyCmd.Result}");
                
                if (!string.IsNullOrEmpty(verifyCmd.Error))
                {
                    Log($"Warning during verification: {verifyCmd.Error}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error during file upload: {ex.Message}");
            }
            
            return;
        }

        private Task UploadItemAsync(SshClient client, VpsConnection connection, string localPath, string remotePath, Action<double, string>? progressCallback = null, double startProgress = 0.0, double endProgress = 100.0)
            {
                var itemName = Path.GetFileName(localPath);
            Log($"Starting transfer for: {itemName}");
            progressCallback?.Invoke(startProgress, $"Preparing transfer: {itemName}");

            var remoteDir = Directory.Exists(localPath) ? remotePath : GetUnixDirectoryName(remotePath);
                if (!string.IsNullOrEmpty(remoteDir))
                {
                using var mkdirCmd = client.RunCommand($"mkdir -p {remoteDir}");
                    if (mkdirCmd.ExitStatus != 0)
                    {
                        Log($"Warning: Could not create remote directory {remoteDir}: {mkdirCmd.Error}");
                    }
                }

            if (Directory.Exists(localPath))
            {
                var tempTarPath = Path.GetTempFileName();
                try
                {
                    // Calculate directory hash before creating tar
                    var localHash = GetDirectoryHash(localPath);
                    
                    // Check if content has changed
                    var remoteHashPath = $"{remotePath}/.content.md5";
                    var remoteHashCommand = $"cat {remoteHashPath} 2>/dev/null || echo ''";
                    using var hashCmd = client.RunCommand(remoteHashCommand);
                    var remoteHash = hashCmd.Result.Trim();

                    if (localHash == remoteHash && !string.IsNullOrEmpty(remoteHash))
                    {
                        Log($"Directory contents unchanged for {itemName}. Skipping upload.");
                        progressCallback?.Invoke(endProgress, $"Skipped: {itemName} (unchanged)");
                        return Task.CompletedTask;
                    }

                    // Create tar archive for upload
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo =
                        {
                            FileName = "tar",
                            Arguments = $"-czf \"{tempTarPath}\" -C \"{localPath}\" .",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        Log($"Error creating tar archive for {localPath}: {error}");
                        throw new Exception($"tar failed: {error}");
                    }
                    var hashFilePath = $"{remotePath}.md5";
                    var remoteHashCommand2 = $"cat {hashFilePath}";
                    using var hashCmd2 = client.RunCommand(remoteHashCommand2);
                    var remoteHash2 = hashCmd2.Result.Trim();

                    if (localHash == remoteHash)
                    {
                        Log($"Hashes match for {itemName}. Skipping upload.");
                        progressCallback?.Invoke(endProgress, $"Skipped: {itemName}");
                        return Task.CompletedTask;
                    }

                    using var sftp = new SftpClient(client.ConnectionInfo);
                    sftp.Connect();
                    var remoteTarPath = $"/tmp/{Guid.NewGuid()}.tar.gz";
                    using (var fileStream = File.OpenRead(tempTarPath))
                    {
                        sftp.UploadFile(fileStream, remoteTarPath, (sent) => {
                            var progress = startProgress + (endProgress - startProgress) * 0.8 * sent / fileStream.Length;
                            progressCallback?.Invoke(progress, $"Uploading {itemName}...");
                        });
                    }

                    var extractCommand = $"tar -xzf {remoteTarPath} -C {remotePath} && rm {remoteTarPath}";
                    using (var cmd = client.RunCommand(extractCommand))
                    {
                        if (cmd.ExitStatus != 0)
                        {
                            Log($"Error extracting {remoteTarPath} on server: {cmd.Error}");
                            throw new Exception($"Extraction failed: {cmd.Error}");
                        }
                    }
                    
                    var storeHashCommand = $"echo '{localHash}' > {remoteHashPath}";
                    client.RunCommand(storeHashCommand);

                    sftp.Disconnect();
                }
                finally
                {
                    File.Delete(tempTarPath);
                }
            }
            else if (File.Exists(localPath))
            {
                var localHash = GetFileMd5(localPath);
                var remoteHashPath = $"{remotePath}.md5";
                var remoteHashCommand = $"cat {remoteHashPath}";
                using var hashCmd = client.RunCommand(remoteHashCommand);
                var remoteHash = hashCmd.Result.Trim();

                if (localHash == remoteHash)
                {
                    Log($"Hashes match for {itemName}. Skipping upload.");
                    progressCallback?.Invoke(endProgress, $"Skipped: {itemName}");
                    return Task.CompletedTask;
                }
                
                using var sftp = new SftpClient(client.ConnectionInfo);
                sftp.Connect();
                using (var fileStream = File.OpenRead(localPath))
                {
                    var remoteFilePath = remotePath.Replace("\\", "/");
                    sftp.UploadFile(fileStream, remoteFilePath, (sent) => {
                        var progress = startProgress + (endProgress - startProgress) * sent / fileStream.Length;
                        progressCallback?.Invoke(progress, $"Uploading {itemName}...");
                    });
                }
                var storeHashCommand = $"echo '{localHash}' > {remoteHashPath}";
                client.RunCommand(storeHashCommand);
                sftp.Disconnect();
            }
            else
            {
                Log($"Error: Neither file nor directory exists at {localPath}");
                throw new Exception($"Local path does not exist: {localPath}");
            }

            progressCallback?.Invoke(endProgress, $"Completed: {itemName}");
            Log($"Successfully transferred: {itemName}");
            return Task.CompletedTask;
        }
    }
}