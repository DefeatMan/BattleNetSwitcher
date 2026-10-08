#nullable enable
using System;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>快照的运行时视图（非序列化对象）。</summary>
    internal sealed class SnapshotInfo
    {
        public string Email { get; init; } = "";
        public string Region { get; init; } = "CN";
        public string DirectoryPath { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public DateTime UpdatedUtc { get; init; }
        public int FileCount { get; init; }
        public int UniqueIdCount { get; init; }

        public override string ToString()
            => $"{Email} [{Region}] 更新于 {UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    }
}
