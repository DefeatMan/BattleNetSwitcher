#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BattleNetSwitcher.Core.Snapshots;
using Microsoft.Win32;

namespace BattleNetSwitcher.Core
{
    /// <summary>
    /// 本地状态快照管理：把战网客户端的"关键文件 + UnifiedAuth 注册表子树"
    /// 保存到工具缓存目录，切换账号时覆盖回去，让客户端直接续期而不弹浏览器。
    ///
    /// 存储布局（按 邮箱 + 区服 二维键）：
    ///   %APPDATA%\BattleNetSwitcher\Snapshots\&lt;邮箱安全化&gt;__&lt;REGION&gt;\
    ///     ├── manifest.json
    ///     ├── registry.json
    ///     └── files\
    ///
    /// 首次访问时会尝试迁移旧格式（&lt;邮箱安全化&gt;\）的目录到新格式。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class SnapshotManager
    {
        // ------------------------------------------------------------
        //  常量
        // ------------------------------------------------------------
        public const string UnifiedAuthSubPath =
            @"Software\Blizzard Entertainment\Battle.net\UnifiedAuth";

        public const string BattleNetRootSubPath =
            @"Software\Blizzard Entertainment\Battle.net";

        private static readonly HashSet<string> ProtectedSubKeyNames = new(
            new[] { "EncryptionKey", "CacheDatabase" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // 旧格式迁移：进程内只跑一次
        private static readonly object LegacyMigrateLock = new();
        private static bool _legacyMigrated;

        // ------------------------------------------------------------
        //  查询
        // ------------------------------------------------------------
        public static bool Exists(string email, string region)
            => GetInfo(email, region) != null;

        public static SnapshotInfo? GetInfo(string email, string region)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            email = email.Trim();

            MigrateLegacyOnce();

            string dir = SnapshotPaths.ForEmail(email, region);
            return ReadInfo(dir);
        }

        /// <summary>列出所有快照（跨邮箱、跨区服）。</summary>
        public static IReadOnlyList<SnapshotInfo> List()
        {
            MigrateLegacyOnce();

            var result = new List<SnapshotInfo>();
            string root = SnapshotPaths.RootDir;

            try
            {
                if (!Directory.Exists(root)) return result;

                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    var info = ReadInfo(dir);
                    if (info != null) result.Add(info);
                }
            }
            catch { /* 忽略枚举失败 */ }

            // 去重：同一 (email, region) 只保留最新的一份
            return result
                .GroupBy(s => (Email: s.Email.ToLowerInvariant(),
                               Region: s.Region.ToUpperInvariant()))
                .Select(g => g.OrderByDescending(s => s.UpdatedUtc).First())
                .OrderBy(s => s.Email, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Region, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>列出某个邮箱在所有区服的快照。</summary>
        public static IReadOnlyList<SnapshotInfo> ListForEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return Array.Empty<SnapshotInfo>();
            email = email.Trim();

            return List()
                .Where(s => string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static SnapshotInfo? ReadInfo(string snapshotDir)
        {
            try
            {
                string manifestFile = SnapshotPaths.ManifestFile(snapshotDir);
                if (!File.Exists(manifestFile)) return null;

                var m = JsonSerializer.Deserialize<SnapshotManifest>(
                    File.ReadAllText(manifestFile), JsonOpts);
                if (m == null) return null;

                return new SnapshotInfo
                {
                    Email = m.Email,
                    Region = SnapshotPaths.NormalizeRegion(m.Region),
                    DirectoryPath = snapshotDir,
                    CreatedUtc = m.CreatedUtc,
                    UpdatedUtc = m.UpdatedUtc,
                    FileCount = m.Files.Count,
                    UniqueIdCount = m.UniqueIds.Count
                };
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        //  保存
        // ------------------------------------------------------------
        public static SnapshotInfo Save(string email, string region, Action<string>? log = null)
        {
            void L(string m) => log?.Invoke(m);

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("邮箱不能为空。", nameof(email));

            email = email.Trim();
            region = SnapshotPaths.NormalizeRegion(region);

            MigrateLegacyOnce();

            Directory.CreateDirectory(SnapshotPaths.RootDir);

            string finalDir = SnapshotPaths.ForEmail(email, region);
            string tempDir = SnapshotPaths.TempForEmail(email, region);

            TryDeleteDirectory(tempDir);

            try
            {
                Directory.CreateDirectory(tempDir);
                string filesDir = SnapshotPaths.FilesDir(tempDir);
                Directory.CreateDirectory(filesDir);

                // ---- 1. 复制文件 ----
                var fileEntries = new List<SnapshotFileEntry>();
                foreach (var src in EnumerateBattleNetFiles())
                {
                    try
                    {
                        string fileName = Path.GetFileName(src);
                        string dst = Path.Combine(filesDir, fileName);
                        File.Copy(src, dst, overwrite: true);

                        var fi = new FileInfo(dst);
                        fileEntries.Add(new SnapshotFileEntry
                        {
                            SourcePath = src,
                            RelativePath = Path.Combine("files", fileName),
                            Sha256 = ComputeSha256(dst),
                            Size = fi.Length,
                            LastWriteUtc = File.GetLastWriteTimeUtc(src)
                        });
                        L($"已复制文件: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        L($"跳过文件 {Path.GetFileName(src)}: {ex.Message}");
                    }
                }

                if (fileEntries.Count == 0)
                    throw new InvalidOperationException(
                        "没有找到任何可快照的战网文件（至少需要 Battle.net.config）。");

                // ---- 2. 导出注册表 ----
                RegistryKeySnapshot regSnap;
                using (var unifiedAuth = Registry.CurrentUser.OpenSubKey(
                           UnifiedAuthSubPath, writable: false))
                {
                    if (unifiedAuth == null)
                    {
                        L("注册表中没有 UnifiedAuth 子键（客户端可能从未成功登录过）。");
                        regSnap = new RegistryKeySnapshot { Name = "UnifiedAuth" };
                    }
                    else
                    {
                        regSnap = RegistrySnapshotIO.Export(
                            unifiedAuth, "UnifiedAuth", ProtectedSubKeyNames);
                        L($"已导出 UnifiedAuth，共 {regSnap.SubKeys.Count} 个子键。");
                    }
                }

                File.WriteAllText(
                    SnapshotPaths.RegistryFile(tempDir),
                    JsonSerializer.Serialize(regSnap, JsonOpts),
                    new UTF8Encoding(false));

                // ---- 3. 提取 uniqueIds 与 SavedAccountNames ----
                var uniqueIds = regSnap.SubKeys
                    .Select(s => s.Name)
                    .Where(IsHex8)
                    .ToList();

                var savedNames = new List<string>();
                try { savedNames = AccountSwitcher.LoadAccounts(); }
                catch { /* 忽略 */ }

                // ---- 4. 写 manifest ----
                var manifest = new SnapshotManifest
                {
                    SchemaVersion = 1,
                    Email = email,
                    SafeEmail = SafeName.FromEmail(email),
                    Region = region,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    SourceUser = $"{Environment.UserDomainName}\\{Environment.UserName}",
                    Files = fileEntries,
                    RegistryFile = "registry.json",
                    RegistryRoots = new List<string>
                    {
                        "HKEY_CURRENT_USER\\" + UnifiedAuthSubPath
                    },
                    ProtectedRegistryPaths = new List<string>
                    {
                        "HKEY_CURRENT_USER\\" + BattleNetRootSubPath + "\\EncryptionKey",
                        "HKEY_CURRENT_USER\\" + BattleNetRootSubPath + "\\CacheDatabase"
                    },
                    UniqueIds = uniqueIds,
                    SavedAccountNames = savedNames
                };

                File.WriteAllText(
                    SnapshotPaths.ManifestFile(tempDir),
                    JsonSerializer.Serialize(manifest, JsonOpts),
                    new UTF8Encoding(false));

                // ---- 5. 原子替换 ----
                if (Directory.Exists(finalDir))
                    Directory.Delete(finalDir, recursive: true);

                Directory.Move(tempDir, finalDir);
                L($"快照已保存: {finalDir}");

                return new SnapshotInfo
                {
                    Email = email,
                    Region = region,
                    DirectoryPath = finalDir,
                    CreatedUtc = manifest.CreatedUtc,
                    UpdatedUtc = manifest.UpdatedUtc,
                    FileCount = fileEntries.Count,
                    UniqueIdCount = uniqueIds.Count
                };
            }
            catch
            {
                TryDeleteDirectory(tempDir);
                throw;
            }
        }

        // ------------------------------------------------------------
        //  恢复
        // ------------------------------------------------------------
        public static void Restore(string email, string region, Action<string>? log = null)
        {
            void L(string m) => log?.Invoke(m);

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("邮箱不能为空。", nameof(email));

            email = email.Trim();
            region = SnapshotPaths.NormalizeRegion(region);

            MigrateLegacyOnce();

            string dir = SnapshotPaths.ForEmail(email, region);
            string manifestFile = SnapshotPaths.ManifestFile(dir);

            if (!File.Exists(manifestFile))
                throw new InvalidOperationException(
                    $"未找到 {email} [{region}] 的快照。");

            var manifest = JsonSerializer.Deserialize<SnapshotManifest>(
                File.ReadAllText(manifestFile), JsonOpts)
                ?? throw new InvalidDataException("无法解析快照 manifest.json。");

            // ---- 1. 恢复文件 ----
            foreach (var entry in manifest.Files)
            {
                string src = Path.Combine(dir, entry.RelativePath);
                if (!File.Exists(src))
                {
                    L($"快照中缺少文件: {entry.RelativePath}");
                    continue;
                }

                string dst = entry.SourcePath;
                string? dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dstDir))
                    Directory.CreateDirectory(dstDir);

                File.Copy(src, dst, overwrite: true);
                L($"已恢复文件: {dst}");
            }

            // ---- 2. 恢复注册表 ----
            string regFile = Path.Combine(dir, manifest.RegistryFile);
            if (!File.Exists(regFile))
            {
                L("快照中没有 registry.json，跳过注册表恢复。");
                return;
            }

            var regSnap = JsonSerializer.Deserialize<RegistryKeySnapshot>(
                File.ReadAllText(regFile), JsonOpts)
                ?? throw new InvalidDataException("无法解析 registry.json。");

            using var key = Registry.CurrentUser.CreateSubKey(UnifiedAuthSubPath, writable: true)
                ?? throw new InvalidOperationException(
                    "无法打开 HKCU\\" + UnifiedAuthSubPath + "。");

            RegistrySnapshotIO.Restore(key, regSnap, ProtectedSubKeyNames);
            L($"已恢复 UnifiedAuth，共 {regSnap.SubKeys.Count} 个子键。");
        }

        // ------------------------------------------------------------
        //  删除
        // ------------------------------------------------------------
        /// <summary>删除某个 (邮箱, 区服) 的快照。</summary>
        public static bool Remove(string email, string region)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            email = email.Trim();
            region = SnapshotPaths.NormalizeRegion(region);

            MigrateLegacyOnce();

            string dir = SnapshotPaths.ForEmail(email, region);
            if (!Directory.Exists(dir)) return false;

            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>删除某个邮箱在所有区服的快照，返回删除的目录数。</summary>
        public static int RemoveAll(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return 0;
            email = email.Trim();

            MigrateLegacyOnce();

            string root = SnapshotPaths.RootDir;
            if (!Directory.Exists(root)) return 0;

            string prefix = SnapshotPaths.EmailPrefix(email);
            int removed = 0;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    Directory.Delete(dir, recursive: true);
                    removed++;
                }
                catch { /* 单个失败不影响其它 */ }
            }

            return removed;
        }

        // ------------------------------------------------------------
        //  旧格式迁移（一次性、幂等）
        // ------------------------------------------------------------
        private static void MigrateLegacyOnce()
        {
            if (_legacyMigrated) return;

            lock (LegacyMigrateLock)
            {
                if (_legacyMigrated) return;

                try
                {
                    string root = SnapshotPaths.RootDir;
                    if (Directory.Exists(root))
                    {
                        foreach (var dir in Directory.EnumerateDirectories(root))
                        {
                            string name = Path.GetFileName(dir);
                            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string manifestFile = SnapshotPaths.ManifestFile(dir);
                            if (!File.Exists(manifestFile)) continue;

                            SnapshotManifest? m;
                            try
                            {
                                m = JsonSerializer.Deserialize<SnapshotManifest>(
                                    File.ReadAllText(manifestFile), JsonOpts);
                            }
                            catch { continue; }

                            if (m == null) continue;

                            string safeEmail = string.IsNullOrWhiteSpace(m.SafeEmail)
                                ? SafeName.FromEmail(m.Email)
                                : m.SafeEmail;
                            string region = SnapshotPaths.NormalizeRegion(m.Region);

                            string expected = safeEmail
                                              + SnapshotPaths.RegionSeparator
                                              + region;

                            // 已经是新格式
                            if (name.Equals(expected, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string newDir = Path.Combine(root, expected);

                            // 新目录已存在：跳过，不动旧的（避免覆盖）
                            if (Directory.Exists(newDir)) continue;

                            try
                            {
                                Directory.Move(dir, newDir);
                            }
                            catch { /* 忽略单个失败 */ }
                        }
                    }
                }
                catch { /* 整体迁移失败也不阻塞 */ }
                finally
                {
                    _legacyMigrated = true;
                }
            }
        }

        // ------------------------------------------------------------
        //  内部工具
        // ------------------------------------------------------------
        private static IEnumerable<string> EnumerateBattleNetFiles()
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string configPath = AccountSwitcher.GetConfigPath();
                string? configDir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(configDir)) dirs.Add(configDir);
            }
            catch { }

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battle.net");
            dirs.Add(defaultDir);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (var file in files)
                {
                    if (seen.Add(file)) yield return file;
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool IsHex8(string s)
        {
            if (s.Length != 8) return false;
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9')
                       || (c >= 'a' && c <= 'f')
                       || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { /* 忽略 */ }
        }
    }
}
