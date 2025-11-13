using System.Collections.Generic;

namespace QuakeServerManager.Models
{
    /// <summary>
    /// Represents the manifest.json file from the community map repository.
    /// Used to track available maps and version information.
    /// </summary>
    public class MapManifest
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public List<MapInfo> Maps { get; set; } = new List<MapInfo>();
    }

    /// <summary>
    /// Information about an individual map in the repository.
    /// </summary>
    public class MapInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string GameType { get; set; } = string.Empty; // e.g., "CTF", "FFA", "Duel"
        public long FileSizeBytes { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    }
}
