using Newtonsoft.Json;
using QuakeServerManager.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace QuakeServerManager.Services
{
    public class DataService
    {
        private const string ProfilesDirectory = "profiles";
        private const string SettingsFile = "settings.json";

        public DataService()
        {
            EnsureDirectoriesExist();
        }

        public async Task<ObservableCollection<VpsConnection>> LoadVpsConnectionsAsync()
        {
            var connections = new ObservableCollection<VpsConnection>();
            
            if (!Directory.Exists(ProfilesDirectory))
                return connections;

            // Clean up any invalid connections first
            await CleanupInvalidConnectionsAsync();

            foreach (var profileDir in Directory.GetDirectories(ProfilesDirectory))
            {
                var metaFile = Path.Combine(profileDir, "meta.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metaFile);
                        var connection = JsonConvert.DeserializeObject<VpsConnection>(json);
                        if (connection != null)
                        {
                            connection.Password = CryptoHelper.Unprotect(connection.Password);
                        }
                        if (connection != null && !string.IsNullOrWhiteSpace(connection.Name))
                        {
                            connections.Add(connection);
                        }
                        else if (connection != null && string.IsNullOrWhiteSpace(connection.Name))
                        {
                            // Clean up invalid connection files
                            try
                            {
                                Directory.Delete(profileDir, true);
                            }
                            catch (Exception)
                            {
                                // Ignore cleanup errors
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip corrupted files
                    }
                }
            }

            return connections;
        }

        private async Task CleanupInvalidConnectionsAsync()
        {
            if (!Directory.Exists(ProfilesDirectory))
                return;

            foreach (var profileDir in Directory.GetDirectories(ProfilesDirectory))
            {
                var metaFile = Path.Combine(profileDir, "meta.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metaFile);
                        var connection = JsonConvert.DeserializeObject<VpsConnection>(json);
                        
                        // Remove connections with empty or null names
                        if (connection == null || string.IsNullOrWhiteSpace(connection.Name))
                        {
                            try
                            {
                                Directory.Delete(profileDir, true);
                            }
                            catch (Exception)
                            {
                                // Ignore cleanup errors
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip corrupted files
                    }
                }
            }
        }

        public async Task SaveVpsConnectionAsync(VpsConnection connection)
        {
            if (connection == null || string.IsNullOrWhiteSpace(connection.Name))
            {
                throw new ArgumentException("VPS connection must have a valid name.");
            }

            var profileDir = Path.Combine(ProfilesDirectory, connection.Name);
            Directory.CreateDirectory(profileDir);

            // Clone the object so we can store an encrypted password on disk
            var connectionToSave = new VpsConnection
            {
                Name = connection.Name,
                Ip = connection.Ip,
                Port = connection.Port,
                Username = connection.Username,
                Password = CryptoHelper.Protect(connection.Password),
                PrivateKeyPath = connection.PrivateKeyPath,
                AuthMethod = connection.AuthMethod
            };

            var metaFile = Path.Combine(profileDir, "meta.json");
            var json = JsonConvert.SerializeObject(connectionToSave, Formatting.Indented);
            await File.WriteAllTextAsync(metaFile, json);
        }

        public Task DeleteVpsConnectionAsync(string connectionName)
        {
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
            }
            return Task.CompletedTask;
        }

        public async Task<ObservableCollection<ServerInstance>> LoadServerInstancesAsync(string connectionName)
        {
            var instances = new ObservableCollection<ServerInstance>();
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            
            if (!Directory.Exists(profileDir))
                return instances;

            // Clean up any invalid instances first
            await CleanupInvalidServerInstancesAsync(connectionName);

            foreach (var file in Directory.GetFiles(profileDir, "*.json"))
            {
                if (Path.GetFileName(file) == "meta.json")
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var instance = JsonConvert.DeserializeObject<ServerInstance>(json);
                    if (instance != null && !string.IsNullOrWhiteSpace(instance.Name))
                    {
                        // Decrypt RCON password
                        instance.RconPassword = CryptoHelper.Unprotect(instance.RconPassword);
                        instances.Add(instance);
                    }
                    else if (instance != null && string.IsNullOrWhiteSpace(instance.Name))
                    {
                        // Clean up invalid instance files
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception)
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip corrupted files
                }
            }

            return instances;
        }

        private async Task CleanupInvalidServerInstancesAsync(string connectionName)
        {
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            if (!Directory.Exists(profileDir))
                return;

            foreach (var file in Directory.GetFiles(profileDir, "*.json"))
            {
                if (Path.GetFileName(file) == "meta.json")
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var instance = JsonConvert.DeserializeObject<ServerInstance>(json);
                    
                    // Remove instances with empty or null names
                    if (instance == null || string.IsNullOrWhiteSpace(instance.Name))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception)
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip corrupted files
                }
            }
        }

        public async Task SaveServerInstanceAsync(string connectionName, ServerInstance instance)
        {
            if (instance == null || string.IsNullOrWhiteSpace(instance.Name))
            {
                throw new ArgumentException("Server instance must have a valid name.");
            }

            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            Directory.CreateDirectory(profileDir);

            // Create a copy with encrypted RCON password for serialization
            var instanceToSave = new ServerInstance
            {
                Name = instance.Name,
                ServerName = instance.ServerName,
                Admin = instance.Admin,
                Location = instance.Location,
                Port = instance.Port,
                MaxClients = instance.MaxClients,
                RconPassword = CryptoHelper.Protect(instance.RconPassword), // Encrypt RCON password
                Map = instance.Map,
                GameType = instance.GameType,
                AdvancedSettings = instance.AdvancedSettings,
                CustomMaps = instance.CustomMaps,
                IsDeployed = instance.IsDeployed,
                IsRunning = instance.IsRunning,
                // DeploymentStatus is computed property, don't copy
                DockerImageTag = instance.DockerImageTag,
                ContainerId = instance.ContainerId
            };

            var instanceFile = Path.Combine(profileDir, $"{instance.Name}.json");
            var json = JsonConvert.SerializeObject(instanceToSave, Formatting.Indented);
            await File.WriteAllTextAsync(instanceFile, json);
        }

        public Task DeleteServerInstanceAsync(string connectionName, string instanceName)
        {
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            var instanceFile = Path.Combine(profileDir, $"{instanceName}.json");
            
            if (File.Exists(instanceFile))
            {
                File.Delete(instanceFile);
            }
            return Task.CompletedTask;
        }

        public async Task<AppSettings> LoadSettingsAsync()
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            try
            {
                var json = await File.ReadAllTextAsync(SettingsFile);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            await File.WriteAllTextAsync(SettingsFile, json);
        }

        public async Task<DeploymentState> LoadDeploymentStateAsync(string connectionName)
        {
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            var deploymentFile = Path.Combine(profileDir, "deployment.json");
            
            if (!File.Exists(deploymentFile))
                return new DeploymentState { VpsConnectionName = connectionName };

            try
            {
                var json = await File.ReadAllTextAsync(deploymentFile);
                var state = JsonConvert.DeserializeObject<DeploymentState>(json);
                return state ?? new DeploymentState { VpsConnectionName = connectionName };
            }
            catch (Exception)
            {
                return new DeploymentState { VpsConnectionName = connectionName };
            }
        }

        public async Task SaveDeploymentStateAsync(string connectionName, DeploymentState state)
        {
            var profileDir = Path.Combine(ProfilesDirectory, connectionName);
            Directory.CreateDirectory(profileDir);

            var deploymentFile = Path.Combine(profileDir, "deployment.json");
            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            await File.WriteAllTextAsync(deploymentFile, json);
        }

        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(ProfilesDirectory);
        }
    }

    public class AppSettings
    {
        public string LastQ3Path { get; set; } = string.Empty;
        public string LastSelectedConnection { get; set; } = string.Empty;
        public string LastSelectedInstance { get; set; } = string.Empty;

        // Docker and first-run settings
        public bool FirstRunCompleted { get; set; } = false;
        public string Pak0Path { get; set; } = string.Empty;

        // Single-image Docker deployment paths (no manual CLI required)
        public string CpmaPath { get; set; } = string.Empty;
        public string ServerExecutablePath { get; set; } = string.Empty;
        public string MapsPath { get; set; } = string.Empty;

        // Map sync settings
        public bool MapSyncEnabled { get; set; } = true;
        public int MapSyncIntervalHours { get; set; } = 24;
        public DateTime LastMapRepoCheck { get; set; } = DateTime.MinValue;
        public bool AutoUpdateMaps { get; set; } = false;

        // Map repository configuration (for community map updates)
        public string MapRepositoryUrl { get; set; } = "https://github.com/YOUR_USERNAME/quacke-maps";
    }
} 