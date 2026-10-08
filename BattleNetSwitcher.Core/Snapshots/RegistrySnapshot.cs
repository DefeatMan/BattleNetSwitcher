#nullable enable
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>
    /// 单个注册表值的快照。
    ///
    /// value 字段的 JSON 表示依 kind 而定：
    ///   REG_SZ / REG_EXPAND_SZ : 字符串
    ///   REG_DWORD              : 数字
    ///   REG_QWORD              : 数字
    ///   REG_MULTI_SZ           : 字符串数组
    ///   REG_BINARY / REG_NONE  : base64 字符串
    /// </summary>
    internal sealed class RegistryValueSnapshot
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "REG_SZ";

        [JsonPropertyName("value")]
        public JsonNode? Value { get; set; }
    }

    /// <summary>单个注册表键（含递归子键）的快照。</summary>
    internal sealed class RegistryKeySnapshot
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("values")]
        public List<RegistryValueSnapshot> Values { get; set; } = new();

        [JsonPropertyName("subKeys")]
        public List<RegistryKeySnapshot> SubKeys { get; set; } = new();
    }
}
