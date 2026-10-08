#nullable enable
using System.Text;

namespace BattleNetSwitcher.Core.Snapshots
{
    /// <summary>
    /// 把"邮箱"转换为 Windows 文件系统安全的目录名。
    /// 规则：
    ///   * 小写化
    ///   * '@' → '_at_'
    ///   * 仅保留 [a-z0-9._-]，其它字符 → '_'
    ///   * 去掉首尾的 '.' / '-' / '_'
    ///   * 空结果 → "default"
    ///   * 超过 100 字符截断
    /// </summary>
    internal static class SafeName
    {
        public static string FromEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "default";

            var sb = new StringBuilder(email.Length + 4);
            foreach (char c in email.ToLowerInvariant())
            {
                if (c == '@')
                {
                    sb.Append("_at_");
                }
                else if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            string result = sb.ToString().Trim('.', '-', '_');
            if (string.IsNullOrEmpty(result)) result = "default";
            if (result.Length > 100) result = result.Substring(0, 100);
            return result;
        }
    }
}
