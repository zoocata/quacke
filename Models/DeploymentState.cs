using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuakeServerManager.Models
{
    public class DeploymentState
    {
        public string VpsConnectionName { get; set; } = string.Empty;
        public List<string> DeployedInstances { get; set; } = new List<string>();
        public DateTime LastDeploymentSync { get; set; } = DateTime.MinValue;

        // Docker-related deployment state
        public string BaseImageVersion { get; set; } = string.Empty;
        public DateTime LastMapSyncDate { get; set; } = DateTime.MinValue;
        public Dictionary<string, string> CustomMapHashes { get; set; } = new Dictionary<string, string>();

        [JsonIgnore]
        public bool HasDeployedInstances => DeployedInstances.Count > 0;
    }
} 