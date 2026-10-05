#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleNetSwitcher.Core
{
    /// <summary>单条“邮箱 + 区服”记录。</summary>
    internal sealed class AccountEntry
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("region")]
        public string Region { get; set; } = "";
    }

    /// <summary>
    /// 本地账号本：管理“邮箱 ↔ 区服”的映射。
    /// 战网自身不记录这个信息，所以由我们本地维护。
    /// 存储：%APPDATA%\BattleNetSwitcher\accounts.json
    /// </summary>
    internal static class AccountBook
    {
        public static string StorageDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BattleNetSwitcher");

        public static string StoragePath { get; } = Path.Combine(StorageDir, "accounts.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static List<AccountEntry>? _cache;

        /// <summary>
        /// 上次读盘时 accounts.json 的最后写入时间（UTC）。
        /// 文件的修改时间变了就说明被外部改过（用户手改 / 其它程序），
        /// 此时必须重新读盘，否则本进程会用旧缓存把别人的改动覆盖回去。
        /// </summary>
        private static DateTime _cacheWriteTimeUtc;

        // ------------------------------------------------------------
        //  读写
        // ------------------------------------------------------------
        public static List<AccountEntry> Load()
        {
            // 缓存按文件修改时间失效：外部改了账号本，本进程必须能感知到
            DateTime writeTimeUtc = GetStorageWriteTimeUtc();

            if (_cache != null && writeTimeUtc == _cacheWriteTimeUtc)
                return _cache;

            try
            {
                if (!File.Exists(StoragePath))
                {
                    _cache = new List<AccountEntry>();
                    _cacheWriteTimeUtc = writeTimeUtc;
                    return _cache;
                }

                string json = File.ReadAllText(StoragePath);
                var list = JsonSerializer.Deserialize<List<AccountEntry>>(json, JsonOptions)
                           ?? new List<AccountEntry>();

                // 去重 + 清洗
                _cache = list
                    .Where(e => !string.IsNullOrWhiteSpace(e.Email) &&
                                !string.IsNullOrWhiteSpace(e.Region))
                    .GroupBy(e => $"{e.Email.ToLowerInvariant()}::{e.Region.ToUpperInvariant()}")
                    .Select(g =>
                    {
                        var f = g.First();
                        f.Region = f.Region.ToUpperInvariant();
                        return f;
                    })
                    .ToList();

                _cacheWriteTimeUtc = writeTimeUtc;
                return _cache;
            }
            catch
            {
                _cache = new List<AccountEntry>();
                _cacheWriteTimeUtc = writeTimeUtc;
                return _cache;
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(StorageDir);
                string json = JsonSerializer.Serialize(
                    _cache ?? new List<AccountEntry>(), JsonOptions);
                File.WriteAllText(StoragePath, json, new UTF8Encoding(false));

                // 同步记下自己写入后的时间，避免下一次 Load 白读一遍
                _cacheWriteTimeUtc = GetStorageWriteTimeUtc();
            }
            catch { /* 忽略写入失败 */ }
        }

        /// <summary>账号本文件的最后写入时间（UTC）；文件不存在时返回 MinValue。</summary>
        private static DateTime GetStorageWriteTimeUtc()
        {
            try
            {
                return File.Exists(StoragePath)
                    ? File.GetLastWriteTimeUtc(StoragePath)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        // ------------------------------------------------------------
        //  查询
        // ------------------------------------------------------------
        public static List<string> GetEmailsForRegion(string region)
        {
            if (string.IsNullOrWhiteSpace(region)) return new List<string>();

            return Load()
                .Where(e => string.Equals(e.Region, region, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetRegionsForEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return new List<string>();

            return Load()
                .Where(e => string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Region)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>所有有账号的区服，按 RegionInfo.All 顺序排列。</summary>
        public static List<string> GetActiveRegions()
        {
            var set = Load()
                .Select(e => e.Region.ToUpperInvariant())
                .Distinct()
                .ToHashSet();

            var result = new List<string>();
            foreach (var r in RegionInfo.All)
            {
                if (set.Remove(r.Code))
                    result.Add(r.Code);
            }

            // 不认识的 region 代码（历史遗留）追加到末尾
            result.AddRange(set.OrderBy(x => x));
            return result;
        }

        public static bool Contains(string email, string region)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(region))
                return false;

            return Load().Any(e =>
                string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Region, region, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------
        //  增删
        // ------------------------------------------------------------
        public static bool Add(string email, string region)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(region))
                return false;

            email = email.Trim();
            region = region.Trim().ToUpperInvariant();

            if (Contains(email, region)) return false;

            Load().Add(new AccountEntry { Email = email, Region = region });
            Save();
            return true;
        }

        public static bool Remove(string email, string region)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(region))
                return false;

            email = email.Trim();
            region = region.Trim().ToUpperInvariant();

            int removed = Load().RemoveAll(e =>
                string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Region, region, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                Save();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 首次初始化：本地账号本为空时，把 Battle.net.config 中的
        /// SavedAccountNames 全部归到当前 SelectedRegion。
        /// </summary>
        public static void EnsureInitialized()
        {
            try
            {
                if (Load().Count > 0) return;

                var (emails, currentRegion) =
                    AccountSwitcher.ReadConfigAccountsAndRegion();

                if (emails.Count == 0) return;

                foreach (var email in emails)
                    Add(email, currentRegion);
            }
            catch { /* 忽略 */ }
        }
    }
}
