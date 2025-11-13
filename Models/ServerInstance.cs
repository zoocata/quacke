using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuakeServerManager.Models
{
    public class ServerInstance : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _admin = string.Empty;
        private string _location = string.Empty;
        private string _serverName = string.Empty;
        private string _rconPassword = string.Empty;
        private int _maxClients = 24;
        private int _port = 27960;
        private string _gameType = "1";
        private string _map = "cpm3a";
        private bool _isRunning = false;
        private bool _isDeployed = false;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Admin
        {
            get => _admin;
            set => SetProperty(ref _admin, value);
        }

        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        public string RconPassword
        {
            get => _rconPassword;
            set => SetProperty(ref _rconPassword, value);
        }

        public int MaxClients
        {
            get => _maxClients;
            set => SetProperty(ref _maxClients, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string GameType
        {
            get => _gameType;
            set => SetProperty(ref _gameType, value);
        }

        public string Map
        {
            get => _map;
            set => SetProperty(ref _map, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public bool IsDeployed
        {
            get => _isDeployed;
            set 
            {
                if (SetProperty(ref _isDeployed, value))
                {
                    // Notify that computed properties have changed
                    OnPropertyChanged(nameof(DeploymentStatus));
                    OnPropertyChanged(nameof(DeploymentStatusDisplay));
                }
            }
        }

        [JsonIgnore]
        public string DeploymentStatus => IsDeployed ? "Deployed" : "Not Deployed";

        [JsonIgnore]
        public string DeploymentStatusDisplay => IsDeployed ? "✅ Deployed" : "❌ Not Deployed";

        // Docker-related properties
        private string _dockerImageTag = string.Empty;
        private string _containerId = string.Empty;
        private List<string> _customMaps = new List<string>();

        public string DockerImageTag
        {
            get => _dockerImageTag;
            set => SetProperty(ref _dockerImageTag, value);
        }

        public string ContainerId
        {
            get => _containerId;
            set => SetProperty(ref _containerId, value);
        }

        public List<string> CustomMaps
        {
            get => _customMaps;
            set => SetProperty(ref _customMaps, value);
        }

        private Dictionary<string, string> _advancedSettings = new()
        {
            { "sv_pure", "1" },
            { "snaps", "30" },
            { "sv_strictAuth", "0" },
            { "server_record", "0" },
            { "server_chatfloodprotect", "0" },
            { "sv_maxrate", "30000" },
            { "sv_allowDownload", "0" },
            { "server_gameplay", "CPM" },
            { "server_maxpacketsmin", "100" },
            { "server_maxpacketsmax", "125" },
            { "server_ratemin", "25000" },
            { "server_optimisebw", "1" },
            { "log_pergame", "0" },
            { "match_readypercent", "100" },
            { "sv_privateClients", "0" },
            { "sv_privatePassword", "" },
            { "sv_restartDelay", "2" }
        };

        public Dictionary<string, string> AdvancedSettings
        {
            get => _advancedSettings;
            set => SetProperty(ref _advancedSettings, value);
        }

        public void UpdateAdvancedSetting(string key, string value)
        {
            if (_advancedSettings.ContainsKey(key))
            {
                _advancedSettings[key] = value;
                OnPropertyChanged(nameof(AdvancedSettings));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
} 