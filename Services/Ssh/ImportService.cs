using QuakeServerManager.Models;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO; // Added for Path.GetFileName and Path.GetFileNameWithoutExtension

namespace QuakeServerManager.Services.Ssh
{
    public class ImportService
    {
        private readonly ConnectionService _connectionService;

        public event Action<string, LogLevel>? LogMessage;

        public ImportService(ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        public async Task<bool> CheckForExistingCpmaInstallationAsync(VpsConnection connection)
        {
            try
            {
                var info = _connectionService.GetConnectionInfo(connection);
                using var client = new SshClient(info);
                await Task.Run(() => client.Connect());
                if (!client.IsConnected) return false;

                var command = "ls /root/cpma/cnq3-server-x64";
                var cmd = await client.ExecuteCommandAsync(command);
                return cmd.ExitStatus == 0;
            }
            catch (Exception ex)
            {
                Log($"Error checking for CPMA installation: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public async Task<List<ServerInstance>> ImportExistingServerConfigurationsAsync(VpsConnection connection)
        {
            var instances = new List<ServerInstance>();
            try
            {
                var info = _connectionService.GetConnectionInfo(connection);
                using var client = new SshClient(info);
                await Task.Run(() => client.Connect());
                if (!client.IsConnected) return instances;

                // Search for all .cfg files in the cpma directory (top level only)
                var command = "find /root/cpma/cpma -maxdepth 1 -name '*.cfg' -type f";
                var cmd = await client.ExecuteCommandAsync(command);

                if (cmd.ExitStatus != 0) return instances;

                var configFiles = cmd.Result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {configFiles.Length} .cfg files to check", LogLevel.Info);
                
                foreach (var configFile in configFiles)
                {
                    var catCmd = await client.ExecuteCommandAsync($"cat {configFile}");
                    if (catCmd.ExitStatus == 0)
                    {
                        var configContent = catCmd.Result;
                        
                        // Validate that this is actually a server configuration file
                        if (!IsServerConfigFile(configContent))
                        {
                            Log($"Skipping {configFile} - not a valid server configuration file", LogLevel.Warning);
                            continue;
                        }
                        
                        // Extract instance name from the filename (remove .cfg extension)
                        var fileName = Path.GetFileName(configFile);
                        var instanceName = Path.GetFileNameWithoutExtension(fileName);
                        
                        // If the filename is just "server.cfg", use a default name
                        if (instanceName.Equals("server", StringComparison.OrdinalIgnoreCase))
                        {
                            instanceName = "default";
                        }
                        
                        var instance = new ServerInstance { Name = instanceName };
                        
                        // Parse server configuration - map all relevant fields
                        instance.ServerName = ParseValue(configContent, "sv_hostname");
                        instance.RconPassword = ParseValue(configContent, "Rconpassword");
                        instance.Admin = ParseValue(configContent, "sv_admin");
                        instance.Location = ParseValue(configContent, "sv_location");
                        
                        // Parse numeric values
                        var maxClientsStr = ParseValue(configContent, "sv_maxclients");
                        if (int.TryParse(maxClientsStr, out var maxClients))
                        {
                            instance.MaxClients = maxClients;
                        }
                        
                        var portStr = ParseValue(configContent, "sv_port");
                        if (int.TryParse(portStr, out var port))
                        {
                            instance.Port = port;
                        }
                        
                        instance.GameType = ParseValue(configContent, "g_gametype");
                        instance.Map = ParseValue(configContent, "map");
                        
                        // Parse class selection (server-wide setting)
                        var defaultClass = ParseValue(configContent, "g_defaultClass");
                        if (!string.IsNullOrWhiteSpace(defaultClass))
                        {
                            // Find the class by name and set it as the server-wide class
                            var availableClasses = GetAvailableClasses();
                            var selectedClass = availableClasses.FirstOrDefault(c => 
                                c.Name.Equals(defaultClass, StringComparison.OrdinalIgnoreCase) ||
                                c.Name.Equals(defaultClass + ".cfg", StringComparison.OrdinalIgnoreCase));
                            
                            if (selectedClass != null)
                            {
                                connection.SelectedClass = selectedClass;
                                Log($"Set server-wide class to: {selectedClass.Name}", LogLevel.Info);
                            }
                        }
                        
                        // Parse advanced settings
                        var advancedSettings = new Dictionary<string, string>
                        {
                            { "sv_pure", ParseValue(configContent, "sv_pure") },
                            { "snaps", ParseValue(configContent, "snaps") },
                            { "sv_strictAuth", ParseValue(configContent, "sv_strictAuth") },
                            { "server_record", ParseValue(configContent, "server_record") },
                            { "server_chatfloodprotect", ParseValue(configContent, "server_chatfloodprotect") },
                            { "sv_maxrate", ParseValue(configContent, "sv_maxrate") },
                            { "sv_allowDownload", ParseValue(configContent, "sv_allowDownload") },
                            { "server_gameplay", ParseValue(configContent, "server_gameplay") },
                            { "server_maxpacketsmin", ParseValue(configContent, "server_maxpacketsmin") },
                            { "server_maxpacketsmax", ParseValue(configContent, "server_maxpacketsmax") },
                            { "server_ratemin", ParseValue(configContent, "server_ratemin") },
                            { "server_optimisebw", ParseValue(configContent, "server_optimisebw") },
                            { "log_pergame", ParseValue(configContent, "log_pergame") },
                            { "match_readypercent", ParseValue(configContent, "match_readypercent") },
                            { "sv_privateClients", ParseValue(configContent, "sv_privateClients") },
                            { "sv_privatePassword", ParseValue(configContent, "sv_privatePassword") },
                            { "sv_restartDelay", ParseValue(configContent, "sv_restartDelay") }
                        };
                        
                        // Only add non-empty values to advanced settings
                        foreach (var setting in advancedSettings)
                        {
                            if (!string.IsNullOrWhiteSpace(setting.Value))
                            {
                                instance.UpdateAdvancedSetting(setting.Key, setting.Value);
                            }
                        }
                        
                        instances.Add(instance);
                        Log($"Imported server configuration: {instanceName} from {fileName}", LogLevel.Success);
                    }
                }
                
                Log($"Import completed. Found {instances.Count} valid server configurations", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Log($"Error importing server configurations: {ex.Message}", LogLevel.Error);
            }
            return instances;
        }

        public async Task<List<Class>> ImportClassConfigurationsAsync(VpsConnection connection)
        {
            var classes = new List<Class>();
            try
            {
                var info = _connectionService.GetConnectionInfo(connection);
                using var client = new SshClient(info);
                await Task.Run(() => client.Connect());
                if (!client.IsConnected) return classes;

                // Search for all .cfg files in the classes directory
                var command = "find /root/cpma/cpma/classes -maxdepth 1 -name '*.cfg' -type f";
                var cmd = await client.ExecuteCommandAsync(command);

                if (cmd.ExitStatus != 0) return classes;

                var classFiles = cmd.Result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Log($"Found {classFiles.Length} class configuration files to check", LogLevel.Info);
                
                foreach (var classFile in classFiles)
                {
                    var catCmd = await client.ExecuteCommandAsync($"cat {classFile}");
                    if (catCmd.ExitStatus == 0)
                    {
                        var classContent = catCmd.Result;
                        
                        // Validate that this is actually a class configuration file
                        if (!IsClassConfigFile(classContent))
                        {
                            Log($"Skipping {classFile} - not a valid class configuration file", LogLevel.Warning);
                            continue;
                        }
                        
                        // Extract class name from the filename (remove .cfg extension)
                        var fileName = Path.GetFileName(classFile);
                        var className = Path.GetFileNameWithoutExtension(fileName);
                        
                        var classInstance = new Class { Name = className, FileName = fileName };
                        
                        // Parse class configuration - map all relevant fields
                        classInstance.Model = ParseValue(classContent, "m");
                        classInstance.Description = ParseValue(classContent, "d");
                        
                        // Parse numeric values
                        var baseSpeedStr = ParseValue(classContent, "s");
                        if (int.TryParse(baseSpeedStr, out var baseSpeed))
                        {
                            classInstance.BaseSpeed = baseSpeed;
                        }
                        
                        var spawnHealthStr = ParseValue(classContent, "h");
                        if (int.TryParse(spawnHealthStr, out var spawnHealth))
                        {
                            classInstance.SpawnHealth = spawnHealth;
                        }
                        
                        var maxArmourStr = ParseValue(classContent, "a");
                        if (int.TryParse(maxArmourStr, out var maxArmour))
                        {
                            classInstance.MaxArmour = maxArmour;
                        }
                        
                        var armourClassStr = ParseValue(classContent, "ac");
                        if (int.TryParse(armourClassStr, out var armourClass))
                        {
                            classInstance.ArmourClass = armourClass;
                        }
                        
                        var hookTypeStr = ParseValue(classContent, "ht");
                        if (int.TryParse(hookTypeStr, out var hookType))
                        {
                            classInstance.HookType = hookType;
                        }
                        
                        var hookPullStr = ParseValue(classContent, "hp");
                        if (int.TryParse(hookPullStr, out var hookPull))
                        {
                            classInstance.HookPull = hookPull;
                        }
                        
                        var hookSpeedStr = ParseValue(classContent, "hs");
                        if (int.TryParse(hookSpeedStr, out var hookSpeed))
                        {
                            classInstance.HookSpeed = hookSpeed;
                        }
                        
                        var startingWeaponStr = ParseValue(classContent, "sw");
                        if (int.TryParse(startingWeaponStr, out var startingWeapon))
                        {
                            classInstance.StartingWeapon = startingWeapon;
                        }
                        
                        // Parse boolean values
                        var doubleJumpStr = ParseValue(classContent, "jd");
                        classInstance.DoubleJump = !string.IsNullOrWhiteSpace(doubleJumpStr) && doubleJumpStr != "0";
                        
                        var rampJumpStr = ParseValue(classContent, "jr");
                        classInstance.RampJump = !string.IsNullOrWhiteSpace(rampJumpStr) && rampJumpStr != "0";
                        
                        // Parse weapon configurations
                        classInstance.Weapon2 = ParseValue(classContent, "w2");
                        classInstance.Weapon3 = ParseValue(classContent, "w3");
                        classInstance.Weapon4 = ParseValue(classContent, "w4");
                        classInstance.Weapon5 = ParseValue(classContent, "w5");
                        classInstance.Weapon6 = ParseValue(classContent, "w6");
                        classInstance.Weapon7 = ParseValue(classContent, "w7");
                        classInstance.Weapon8 = ParseValue(classContent, "w8");
                        
                        classes.Add(classInstance);
                        Log($"Imported class configuration: {className} from {fileName}", LogLevel.Success);
                    }
                }
                
                Log($"Class import completed. Found {classes.Count} valid class configurations", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Log($"Error importing class configurations: {ex.Message}", LogLevel.Error);
            }
            return classes;
        }

        private string ParseValue(string configContent, string key)
        {
            var line = configContent.Split('\n').FirstOrDefault(l => l.Trim().StartsWith(key + " "));
            if (line == null) return string.Empty;

            // Find the position of the key in the line
            var keyIndex = line.IndexOf(key + " ");
            if (keyIndex == -1) return string.Empty;

            // Get the value part after the key
            var valuePart = line.Substring(keyIndex + key.Length + 1).Trim();
            
            // Handle quoted values
            if (valuePart.StartsWith("\"") && valuePart.EndsWith("\""))
            {
                return valuePart.Substring(1, valuePart.Length - 2);
            }
            
            // Handle unquoted values (take everything until the next space or end of line)
            var spaceIndex = valuePart.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return valuePart.Substring(0, spaceIndex);
            }
            
            return valuePart;
        }

        private bool IsServerConfigFile(string configContent)
        {
            // Check for common server configuration keywords that indicate this is a server config
            var serverKeywords = new[]
            {
                "sv_hostname",      // Server hostname
                "Rconpassword",     // RCON password
                "sv_maxclients",    // Max clients
                "sv_privateclients", // Private clients
                "sv_privatepassword", // Private password
                "sv_pure",          // Pure server setting
                "sv_punkbuster",    // PunkBuster setting
                "g_gametype",       // Game type
                "map",              // Map name
                "fraglimit",        // Frag limit
                "timelimit",        // Time limit
                "capturelimit",     // Capture limit
                "sv_allowdownload", // Download settings
                "sv_allowupload",   // Upload settings
                "sv_floodprotect",  // Flood protection
                "sv_strictAuth",    // Authentication
                "sv_privatePassword" // Private password (alternative spelling)
            };
            
            // Count how many server keywords are present
            var keywordCount = serverKeywords.Count(keyword => 
                configContent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            // Consider it a server config if it has at least 2 server-specific keywords
            // This helps avoid false positives from other .cfg files
            return keywordCount >= 2;
        }

        private bool IsClassConfigFile(string configContent)
        {
            // Check for common class configuration keywords that indicate this is a class config
            var classKeywords = new[]
            {
                "m", // Model
                "d", // Description
                "s", // Base speed
                "h", // Spawn health
                "a", // Max armour
                "ac", // Armour class
                "ht", // Hook type
                "hp", // Hook pull
                "hs", // Hook speed
                "sw", // Starting weapon
                "jd", // Double jump
                "jr", // Ramp jump
                "w2", "w3", "w4", "w5", "w6", "w7", "w8" // Weapon slots
            };
            
            // Count how many class keywords are present
            var keywordCount = classKeywords.Count(keyword => 
                configContent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            // Consider it a class config if it has at least 2 class-specific keywords
            // This helps avoid false positives from other .cfg files
            return keywordCount >= 2;
        }

        private List<Class> GetAvailableClasses()
        {
            return new List<Class>
            {
                new Class 
                { 
                    Name = "Scout",
                    Model = "slash",
                    BaseSpeed = 320,
                    SpawnHealth = 100,
                    MaxArmour = 50,
                    ArmourClass = 0,
                    DoubleJump = true,
                    RampJump = true,
                    Weapon3 = "15,25,5,10", // Shotgun
                    Weapon4 = "3,6,1,3",    // Machine Gun
                    StartingWeapon = 3,
                    FileName = "scout.cfg"
                },
                new Class 
                { 
                    Name = "Fighter",
                    Model = "sarge",
                    BaseSpeed = 280,
                    SpawnHealth = 100,
                    MaxArmour = 100,
                    ArmourClass = 1,
                    DoubleJump = true,
                    RampJump = false,
                    Weapon3 = "10,25,5,10", // Shotgun
                    Weapon5 = "5,25,5,10",  // Grenade Launcher
                    Weapon8 = "50,100,25,50", // Plasma Gun
                    StartingWeapon = 3,
                    FileName = "fighter.cfg"
                },
                new Class 
                { 
                    Name = "Sniper",
                    Model = "visor",
                    BaseSpeed = 280,
                    SpawnHealth = 100,
                    MaxArmour = 50,
                    ArmourClass = 1,
                    DoubleJump = true,
                    RampJump = false,
                    Weapon2 = "100,100,25,50", // Railgun
                    Weapon7 = "10,25,5,10",    // Lightning Gun
                    StartingWeapon = 3,
                    FileName = "sniper.cfg"
                },
                new Class 
                { 
                    Name = "Tank",
                    Model = "keel",
                    BaseSpeed = 240,
                    SpawnHealth = 100,
                    MaxArmour = 150,
                    ArmourClass = 2,
                    DoubleJump = false,
                    RampJump = false,
                    Weapon3 = "10,25,5,10",   // Shotgun
                    Weapon5 = "10,25,5,10",   // Grenade Launcher
                    Weapon6 = "50,150,25,50", // Rocket Launcher
                    StartingWeapon = 3,
                    FileName = "tank.cfg"
                }
            };
        }
    }
}
