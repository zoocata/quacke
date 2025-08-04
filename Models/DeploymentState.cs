using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuakeServerManager.Models
{
    public class DeploymentState
    {
        public string VpsConnectionName { get; set; } = string.Empty;
        public List<string> DeployedInstances { get; set; } = new List<string>();
        public DateTime LastDeploymentSync { get; set; } = DateTime.MinValue;
        
        [JsonIgnore]
        public bool HasDeployedInstances => DeployedInstances.Count > 0;
    }
} 