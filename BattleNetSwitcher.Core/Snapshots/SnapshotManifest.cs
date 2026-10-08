#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>manifest.json 中单个文件条目。</summary>
    internal sealed class SnapshotFileEntry
    {
        [JsonPropertyName("sourcePath")]
        public string SourcePath { get; set; } = "";

        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("lastWriteUtc")]
        public DateTime LastWriteUtc { get; set; }
    }

    /// <summary>快照元数据。序列化为 manifest.json。</summary>
    internal sealed class SnapshotManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("safeEmail")]
        public string SafeEmail { get; set; } = "";

        [JsonPropertyName("region")]
        public string Region { get; set; } = "CN";

        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [JsonPropertyName("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = "";

        [JsonPropertyName("sourceUser")]
        public string SourceUser { get; set; } = "";

        [JsonPropertyName("files")]
        public List<SnapshotFileEntry> Files { get; set; } = new();

        [JsonPropertyName("registryFile")]
        public string RegistryFile { get; set; } = "registry.json";

        [JsonPropertyName("registryRoots")]
        public List<string> RegistryRoots { get; set; } = new();

        [JsonPropertyName("protectedRegistryPaths")]
        public List<string> ProtectedRegistryPaths { get; set; } = new();

        [JsonPropertyName("uniqueIds")]
        public List<string> UniqueIds { get; set; } = new();

        [JsonPropertyName("savedAccountNames")]
        public List<string> SavedAccountNames { get; set; } = new();
    }
}
