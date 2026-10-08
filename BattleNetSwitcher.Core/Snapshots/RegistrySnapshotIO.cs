#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>
    /// 注册表快照的递归导出与恢复。
    /// 保护规则：导出/恢复过程中跳过 protectedSubKeyNames 中列出的子键名，
    /// 防止误触 EncryptionKey / CacheDatabase 等安装级密钥。
    /// </summary>
    internal static class RegistrySnapshotIO
    {
        // ------------------------------------------------------------
        //  导出
        // ------------------------------------------------------------
        /// <summary>把 source 键（含递归子键）导出为快照。name 为该键的简单名。</summary>
        public static RegistryKeySnapshot Export(
            RegistryKey source,
            string name,
            HashSet<string> protectedSubKeyNames)
        {
            var snap = new RegistryKeySnapshot { Name = name };

            // 值
            foreach (var valueName in source.GetValueNames())
            {
                try
                {
                    var kind = source.GetValueKind(valueName);
                    var value = source.GetValue(valueName);
                    snap.Values.Add(SerializeValue(valueName, kind, value));
                }
                catch { /* 单个值失败不影响其它值 */ }
            }

            // 子键（递归）
            foreach (var subName in source.GetSubKeyNames())
            {
                if (protectedSubKeyNames.Contains(subName)) continue;

                try
                {
                    using var sub = source.OpenSubKey(subName, writable: false);
                    if (sub == null) continue;

                    snap.SubKeys.Add(Export(sub, subName, protectedSubKeyNames));
                }
                catch { /* 单个子键失败不影响其它子键 */ }
            }

            return snap;
        }

        // ------------------------------------------------------------
        //  恢复
        // ------------------------------------------------------------
        /// <summary>
        /// 把快照写回 target 键。
        /// 会先清空 target 下所有非保护子键及其值，然后按快照重建。
        /// </summary>
        public static void Restore(
            RegistryKey target,
            RegistryKeySnapshot snap,
            HashSet<string> protectedSubKeyNames)
        {
            // 1. 删除现有子键（跳过受保护名）
            foreach (var subName in target.GetSubKeyNames().ToArray())
            {
                if (protectedSubKeyNames.Contains(subName)) continue;
                try { target.DeleteSubKeyTree(subName, throwOnMissingSubKey: false); }
                catch { }
            }

            // 2. 清空根键的值
            foreach (var v in target.GetValueNames().ToArray())
            {
                try { target.DeleteValue(v, throwOnMissingValue: false); }
                catch { }
            }

            // 3. 写入值
            foreach (var v in snap.Values)
            {
                try
                {
                    target.SetValue(v.Name, DeserializeValue(v), ParseKind(v.Kind));
                }
                catch { }
            }

            // 4. 递归写入子键
            foreach (var sub in snap.SubKeys)
            {
                if (protectedSubKeyNames.Contains(sub.Name)) continue;
                try
                {
                    using var child = target.CreateSubKey(sub.Name, writable: true);
                    if (child != null) Restore(child, sub, protectedSubKeyNames);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------
        //  值类型序列化
        // ------------------------------------------------------------
        private static RegistryValueSnapshot SerializeValue(
            string name, RegistryValueKind kind, object? value)
        {
            var snap = new RegistryValueSnapshot
            {
                Name = name,
                Kind = KindToString(kind)
            };

            switch (kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    snap.Value = JsonValue.Create(value as string ?? "");
                    break;

                case RegistryValueKind.DWord:
                    snap.Value = JsonValue.Create(value is int i ? i : 0);
                    break;

                case RegistryValueKind.QWord:
                    snap.Value = JsonValue.Create(value is long l ? l : 0L);
                    break;

                case RegistryValueKind.MultiString:
                    {
                        var arr = value as string[] ?? Array.Empty<string>();
                        var jsonArr = new JsonArray();
                        foreach (var s in arr) jsonArr.Add(JsonValue.Create(s));
                        snap.Value = jsonArr;
                        break;
                    }

                case RegistryValueKind.Binary:
                case RegistryValueKind.None:
                    {
                        var bytes = value as byte[] ?? Array.Empty<byte>();
                        snap.Value = JsonValue.Create(Convert.ToBase64String(bytes));
                        break;
                    }

                default:
                    snap.Value = JsonValue.Create(value?.ToString() ?? "");
                    break;
            }

            return snap;
        }

        /// <summary>
        /// 反序列化为 RegistryKey.SetValue 可接受的非 null 值。
        /// 所有分支都有兜底，保证不会返回 null。
        /// </summary>
        private static object DeserializeValue(RegistryValueSnapshot snap)
        {
            switch (snap.Kind)
            {
                case "REG_SZ":
                case "REG_EXPAND_SZ":
                    return snap.Value?.GetValue<string>() ?? "";

                case "REG_DWORD":
                    return snap.Value?.GetValue<int>() ?? 0;

                case "REG_QWORD":
                    return snap.Value?.GetValue<long>() ?? 0L;

                case "REG_MULTI_SZ":
                    {
                        if (snap.Value is JsonArray arr)
                        {
                            var list = new List<string>(arr.Count);
                            foreach (var item in arr)
                                list.Add(item?.GetValue<string>() ?? "");
                            return list.ToArray();
                        }
                        return Array.Empty<string>();
                    }

                case "REG_BINARY":
                case "REG_NONE":
                    {
                        string b64 = snap.Value?.GetValue<string>() ?? "";
                        try { return Convert.FromBase64String(b64); }
                        catch { return Array.Empty<byte>(); }
                    }

                default:
                    return snap.Value?.ToString() ?? "";
            }
        }

        private static string KindToString(RegistryValueKind kind) => kind switch
        {
            RegistryValueKind.String       => "REG_SZ",
            RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
            RegistryValueKind.DWord        => "REG_DWORD",
            RegistryValueKind.QWord        => "REG_QWORD",
            RegistryValueKind.MultiString  => "REG_MULTI_SZ",
            RegistryValueKind.Binary       => "REG_BINARY",
            RegistryValueKind.None         => "REG_NONE",
            _                              => "REG_SZ"
        };

        private static RegistryValueKind ParseKind(string kind) => kind switch
        {
            "REG_SZ"        => RegistryValueKind.String,
            "REG_EXPAND_SZ" => RegistryValueKind.ExpandString,
            "REG_DWORD"     => RegistryValueKind.DWord,
            "REG_QWORD"     => RegistryValueKind.QWord,
            "REG_MULTI_SZ"  => RegistryValueKind.MultiString,
            "REG_BINARY"    => RegistryValueKind.Binary,
            "REG_NONE"      => RegistryValueKind.None,
            _               => RegistryValueKind.String
        };
    }
}
