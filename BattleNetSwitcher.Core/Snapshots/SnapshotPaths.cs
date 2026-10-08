#nullable enable
using System;
using System.IO;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>
    /// 快照相关路径计算。
    /// 目录命名：&lt;邮箱安全化&gt;__&lt;REGION&gt;
    /// 例：user_example.com__CN / user_example.com__US
    /// </summary>
    internal static class SnapshotPaths
    {
        public const string DefaultRootFolderName = "Snapshots";
        public const string RegionSeparator = "__";

        /// <summary>快照根目录。</summary>
        public static string RootDir
        {
            get
            {
                var custom = AppSettings.Current.SnapshotRootPath;
                if (!string.IsNullOrWhiteSpace(custom))
                    return custom;

                return Path.Combine(AppSettings.StorageDir, DefaultRootFolderName);
            }
        }

        /// <summary>规范化区服代码；空 → "CN"。</summary>
        public static string NormalizeRegion(string? region)
        {
            if (string.IsNullOrWhiteSpace(region)) return "CN";
            return region.Trim().ToUpperInvariant();
        }

        /// <summary>某个 (邮箱, 区服) 对应的正式快照目录。</summary>
        public static string ForEmail(string email, string region)
        {
            string leaf = SafeName.FromEmail(email)
                          + RegionSeparator
                          + NormalizeRegion(region);
            return Path.Combine(RootDir, leaf);
        }

        /// <summary>某个 (邮箱, 区服) 对应的临时目录（保存过程中使用）。</summary>
        public static string TempForEmail(string email, string region)
        {
            string leaf = SafeName.FromEmail(email)
                          + RegionSeparator
                          + NormalizeRegion(region)
                          + ".tmp";
            return Path.Combine(RootDir, leaf);
        }

        /// <summary>某个邮箱在快照目录下的文件名前缀（用于枚举该邮箱的所有区服）。</summary>
        public static string EmailPrefix(string email)
            => SafeName.FromEmail(email) + RegionSeparator;

        public static string ManifestFile(string snapshotDir)
            => Path.Combine(snapshotDir, "manifest.json");

        public static string RegistryFile(string snapshotDir)
            => Path.Combine(snapshotDir, "registry.json");

        public static string FilesDir(string snapshotDir)
            => Path.Combine(snapshotDir, "files");
    }
}
