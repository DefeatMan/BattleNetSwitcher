#nullable enable
using System;
using System.Linq;
using System.Reflection;

namespace BattleNetSwitcher.Core
{
    /// <summary>
    /// 程序元信息：版本号、仓库地址等。
    /// 数据来源：
    ///   * 版本号 —— csproj &lt;Version&gt; → AssemblyInformationalVersion
    ///   * 仓库地址 —— csproj &lt;RepositoryUrl&gt; → AssemblyMetadata("RepositoryUrl")
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>形如 "1.0.0" 的版本号（去掉 build metadata，只保留前三段）。</summary>
        public static string Version { get; } = ReadVersion();

        /// <summary>形如 "https://github.com/xxx/BattleNetSwitcher" 的仓库地址（无尾斜杠）。</summary>
        public static string RepositoryUrl { get; } = ReadRepositoryUrl();

        /// <summary>形如 "https://github.com/xxx/BattleNetSwitcher/releases" 的发布页地址。</summary>
        public static string ReleasesUrl =>
            string.IsNullOrEmpty(RepositoryUrl)
                ? string.Empty
                : RepositoryUrl.TrimEnd('/') + "/releases";

        // ------------------------------------------------------------
        //  版本号
        // ------------------------------------------------------------
        private static string ReadVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                string? raw =
                    asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                    ?? asm.GetName().Version?.ToString();

                if (string.IsNullOrEmpty(raw)) return "0.0.0";

                // 去掉 build metadata（+ 后面的部分）
                int plus = raw.IndexOf('+');
                if (plus > 0) raw = raw.Substring(0, plus);

                // 只保留前三段
                var parts = raw.Split('.');
                if (parts.Length >= 3)
                    return $"{parts[0]}.{parts[1]}.{parts[2]}";

                return raw;
            }
            catch
            {
                return "0.0.0";
            }
        }

        // ------------------------------------------------------------
        //  仓库地址
        // ------------------------------------------------------------
        private static string ReadRepositoryUrl()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // .NET SDK 会把 <RepositoryUrl> 写成 AssemblyMetadata("RepositoryUrl", ...)
                var meta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                              .FirstOrDefault(a => string.Equals(
                                  a.Key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(meta?.Value))
                    return meta!.Value!;

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
