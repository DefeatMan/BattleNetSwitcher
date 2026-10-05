#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace BattleNetSwitcher.Core
{
    /// <summary>
    /// 通过 netsh advfirewall 管理出站阻止规则。
    /// 需要管理员权限（已在 app.manifest 中请求）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class FirewallManager
    {
        /// <summary>创建并启用出站阻止规则（先删除同名旧规则）。</summary>
        public static void CreateBlockRule(string ruleName, string appPath)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("规则名不能为空。", nameof(ruleName));
            if (string.IsNullOrWhiteSpace(appPath))
                throw new ArgumentException("应用路径不能为空。", nameof(appPath));

            RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"", ignoreExitCode: true);
            RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=out action=block program=\"{appPath}\" enable=yes profile=any");
        }

        /// <summary>启用 / 禁用规则。</summary>
        public static void SetRuleEnabled(string ruleName, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return;

            RunNetsh(
                $"advfirewall firewall set rule name=\"{ruleName}\" new enable=" +
                (enabled ? "yes" : "no"));
        }

        /// <summary>删除规则（不存在时不抛异常）。</summary>
        public static void DeleteRule(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return;

            RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"", ignoreExitCode: true);
        }

        /// <summary>判断规则是否存在。</summary>
        public static bool RuleExists(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        //  执行 netsh
        // ------------------------------------------------------------
        private static void RunNetsh(string args, bool ignoreExitCode = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi)
                          ?? throw new InvalidOperationException("无法启动 netsh 进程。");

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();

            if (!p.WaitForExit(10000))
            {
                try { p.Kill(); } catch { /* 忽略 */ }
                throw new TimeoutException("netsh 执行超时（10 秒）。");
            }

            if (!ignoreExitCode && p.ExitCode != 0)
            {
                string msg = (stderr + Environment.NewLine + stdout).Trim();
                throw new InvalidOperationException(
                    $"netsh 执行失败（退出码 {p.ExitCode}）：{msg}{Environment.NewLine}" +
                    "提示：修改防火墙规则需要以【管理员身份】运行本程序。");
            }
        }
    }
}