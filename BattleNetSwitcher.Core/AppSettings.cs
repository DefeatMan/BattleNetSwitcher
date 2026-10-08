#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleNetSwitcher.Core
{
    /// <summary>全局配置的数据结构。</summary>
    internal sealed class AppSettingsData
    {
        /// <summary>战网可执行文件完整路径（为空则走注册表查找）。</summary>
        [JsonPropertyName("battleNetExePath")]
        public string? BattleNetExePath { get; set; }

        /// <summary>Battle.net.config 完整路径（为空则用默认 %APPDATA% 位置）。</summary>
        [JsonPropertyName("battleNetConfigPath")]
        public string? BattleNetConfigPath { get; set; }

        /// <summary>禁用“一键拔线”功能（GUI 启动不再请求 UAC）。</summary>
        [JsonPropertyName("disablePullout")]
        public bool DisablePullout { get; set; }

        // ------------------------------------------------------------
        //  本地状态快照
        // ------------------------------------------------------------
        /// <summary>切换账号时优先恢复本地快照（若该账号有快照）。</summary>
        [JsonPropertyName("useSnapshotOnSwitch")]
        public bool UseSnapshotOnSwitch { get; set; } = true;

        /// <summary>保存快照前是否自动关闭战网客户端（推荐 true）。</summary>
        [JsonPropertyName("closeBattleNetBeforeSave")]
        public bool CloseBattleNetBeforeSave { get; set; } = true;

        /// <summary>自定义快照根目录（为空 = %APPDATA%\BattleNetSwitcher\Snapshots）。</summary>
        [JsonPropertyName("snapshotRootPath")]
        public string? SnapshotRootPath { get; set; }
    }

    /// <summary>
    /// 全局配置：%APPDATA%\BattleNetSwitcher\config.json
    /// 用于覆盖默认的战网路径、禁用拔线功能、快照开关等。
    /// </summary>
    internal static class AppSettings
    {
        public static string StorageDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BattleNetSwitcher");

        public static string StoragePath { get; } = Path.Combine(StorageDir, "config.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static AppSettingsData? _cache;

        public static AppSettingsData Current => _cache ??= Load();

        /// <summary>重新从磁盘读取（外部修改配置后调用）。</summary>
        public static void Reload() => _cache = Load();

        /// <summary>把当前内存里的配置写回磁盘。</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(StorageDir);
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(StoragePath, json, new UTF8Encoding(false));
            }
            catch { /* 忽略写入失败 */ }
        }

        private static AppSettingsData Load()
        {
            try
            {
                if (!File.Exists(StoragePath))
                    return new AppSettingsData();

                string json = File.ReadAllText(StoragePath);
                return JsonSerializer.Deserialize<AppSettingsData>(json, JsonOptions)
                       ?? new AppSettingsData();
            }
            catch
            {
                return new AppSettingsData();
            }
        }
    }
}
