#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Win32;

namespace BattleNetSwitcher.Core
{
    // ============================================================
    //  区域定义
    // ============================================================
    internal enum BattleNetRegion
    {
        CN, US, EU, KR, TW
    }

    internal sealed class RegionInfo
    {
        public BattleNetRegion Region { get; }
        public string Code { get; }
        public string DisplayName { get; }
        public string DefaultLocale { get; }

        public RegionInfo(BattleNetRegion region, string code,
                          string displayName, string defaultLocale)
        {
            Region = region;
            Code = code;
            DisplayName = displayName;
            DefaultLocale = defaultLocale;
        }

        public override string ToString() => DisplayName;

        public static RegionInfo[] All { get; } = new[]
        {
            new RegionInfo(BattleNetRegion.CN, "CN", "国服 (CN)", "zhCN"),
            new RegionInfo(BattleNetRegion.US, "US", "美服 (US)", "enUS"),
            new RegionInfo(BattleNetRegion.EU, "EU", "欧服 (EU)", "enGB"),
            new RegionInfo(BattleNetRegion.KR, "KR", "亚服 (KR)", "koKR"),
            new RegionInfo(BattleNetRegion.TW, "TW", "台服 (TW)", "zhTW"),
        };

        public static RegionInfo? TryFromCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            foreach (var r in All)
            {
                if (string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return null;
        }

        public static RegionInfo FromCode(string? code)
            => TryFromCode(code) ?? All[0];
    }

    // ============================================================
    //  战网账号切换核心
    // ============================================================
    [SupportedOSPlatform("windows")]
    internal static class AccountSwitcher
    {
        // ------------------------------------------------------------
        //  配置文件路径
        // ------------------------------------------------------------
        /// <summary>
        /// Battle.net.config 的完整路径。
        /// 优先用 AppSettings.BattleNetConfigPath；
        /// 未配置则回落到 %APPDATA%\Battle.net\Battle.net.config。
        /// </summary>
        public static string ConfigPath => GetConfigPath();

        /// <summary>获取战网配置文件路径（供调试/诊断）。</summary>
        public static string GetConfigPath()
        {
            var custom = AppSettings.Current.BattleNetConfigPath;
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battle.net", "Battle.net.config");
        }

        /// <summary>默认的配置文件路径（不读配置）。</summary>
        public static string DefaultConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Battle.net", "Battle.net.config");

        // ------------------------------------------------------------
        //  读取账号列表
        // ------------------------------------------------------------
        public static List<string> LoadAccounts()
        {
            string configPath = GetConfigPath();

            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    "未找到 Battle.net.config 文件，请确认战网已安装并登录过。" +
                    "若使用便携版，请在“设置”里指定配置文件路径。", configPath);

            string json = File.ReadAllText(configPath);
            var config = JsonNode.Parse(json)
                         ?? throw new InvalidDataException("配置文件解析失败。");

            var clientNode = config["Client"]
                             ?? throw new InvalidDataException("配置文件结构异常：缺少 Client 节点。");

            string? savedNames = clientNode["SavedAccountNames"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(savedNames))
                return new List<string>();

            return savedNames
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();
        }

        public static (List<string> emails, string region) ReadConfigAccountsAndRegion()
        {
            try
            {
                string configPath = GetConfigPath();
                if (!File.Exists(configPath))
                    return (new List<string>(), "CN");

                var config = JsonNode.Parse(File.ReadAllText(configPath));
                var clientNode = config?["Client"];
                if (clientNode == null) return (new List<string>(), "CN");

                string? savedNames = clientNode["SavedAccountNames"]?.GetValue<string>();
                var emails = string.IsNullOrWhiteSpace(savedNames)
                    ? new List<string>()
                    : savedNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(a => a.Trim())
                                .Where(a => a.Length > 0)
                                .ToList();

                string? region = clientNode["LoginSettings"]?["SelectedRegion"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(region)) region = "CN";

                return (emails, region.ToUpperInvariant());
            }
            catch
            {
                return (new List<string>(), "CN");
            }
        }

        // ------------------------------------------------------------
        //  切换账号（含区域）
        // ------------------------------------------------------------
        public static string SwitchAccount(string targetEmail,
                                           BattleNetRegion region = BattleNetRegion.CN,
                                           bool restart = true,
                                           Action<string>? log = null)
        {
            var sb = new StringBuilder();
            void L(string m)
            {
                if (log != null) log(m);
                else sb.AppendLine(m);
            }

            if (string.IsNullOrWhiteSpace(targetEmail))
                throw new ArgumentException("目标邮箱不能为空。", nameof(targetEmail));

            string configPath = GetConfigPath();
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    "未找到 Battle.net.config 文件。" +
                    "若使用便携版，请在“设置”里指定配置文件路径。", configPath);

            var regionInfo = RegionInfo.FromCode(region.ToString());

            var accounts = LoadAccounts();
            if (accounts.Count == 0)
                throw new InvalidOperationException(
                    "配置中没有保存任何账号。请先在战网客户端中登录一次该账号（勾选“记住密码”）。");

            if (!accounts.Any(a => a.Equals(targetEmail, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"账号 {targetEmail} 不在已保存列表中。" +
                    "请先在战网客户端中登录一次该账号（勾选“记住密码”）。");

            CloseBattleNet(L);

            try
            {
                var again = LoadAccounts();
                if (again.Count > 0) accounts = again;
            }
            catch { }

            if (!accounts.Any(a => a.Equals(targetEmail, StringComparison.OrdinalIgnoreCase)))
                accounts.Insert(0, targetEmail);

            string backupPath = configPath + ".backup";
            try
            {
                File.Copy(configPath, backupPath, overwrite: true);
                L($"已备份原配置到: {backupPath}");
            }
            catch (Exception ex)
            {
                L($"备份失败（继续执行）: {ex.Message}");
            }

            var config = JsonNode.Parse(File.ReadAllText(configPath))
                         ?? throw new InvalidDataException("无法解析配置文件。");

            var clientNode = config["Client"]
                             ?? throw new InvalidDataException("配置文件结构异常：缺少 Client 节点。");

            // 目标账号已经在首位时**不要**重写 SavedAccountNames。
            // 原因：这份列表是客户端自己维护的（登录后会把该账号置顶），
            // 我们每次切换都无脑重排，会让注册表里的登录凭证与配置对不上，
            // 反而把本来还有效的会话搞失效。只在真的需要时才动它。
            bool namesChanged = accounts.Count == 0 ||
                                !accounts[0].Equals(targetEmail, StringComparison.OrdinalIgnoreCase);

            if (namesChanged)
            {
                accounts.RemoveAll(a => a.Equals(targetEmail, StringComparison.OrdinalIgnoreCase));
                accounts.Insert(0, targetEmail);
                clientNode["SavedAccountNames"] = string.Join(",", accounts);
                L("已把目标账号置顶到 SavedAccountNames。");
            }
            else
            {
                L("目标账号已在 SavedAccountNames 首位，保持不变（避免打断已有会话）。");
            }

            TrySetConfigRegion(config, regionInfo, L);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, config.ToJsonString(options), new UTF8Encoding(false));
            L($"已将账号 {targetEmail} 设置为 [{regionInfo.DisplayName}] 的默认登录账号。");

            try
            {
                AccountBook.Add(targetEmail, regionInfo.Code);
            }
            catch (Exception ex)
            {
                L($"写账号本失败（不影响切换）: {ex.Message}");
            }

            if (restart)
            {
                string? exe = GetBattleNetPath();
                if (exe != null && File.Exists(exe))
                {
                    StartBattleNet(exe, regionInfo, L);
                }
                else
                {
                    L("未能找到战网可执行文件。若使用便携版，请在“设置”里指定路径；" +
                      "也可手动打开战网客户端以应用切换。");
                }
            }

            // 收尾说明：让用户知道这次切换后客户端大概会处于什么状态
            if (!namesChanged)
            {
                L("账号已在校验范围内且登录态未变动，客户端应当保持原会话。");
            }
            else
            {
                L("切换完成。若客户端要求重新登录，勾选“记住密码”登录一次即可；" +
                  "战网会话令牌是短命的，且只在客户端运行期间续期，" +
                  "离开客户端越久越可能需要重新登录。");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 直接以指定区服启动战网客户端（不改动默认登录账号）。
        /// 用于"想换区服登录一个新号"：客户端会停在登录页，
        /// 由用户在客户端里自己登录，登完战网会把账号记进 SavedAccountNames。
        /// </summary>
        public static string LaunchForRegion(BattleNetRegion region, Action<string>? log = null)
        {
            var sb = new StringBuilder();
            void L(string m)
            {
                if (log != null) log(m);
                else sb.AppendLine(m);
            }

            var regionInfo = RegionInfo.FromCode(region.ToString());
            L($"正在以 [{regionInfo.DisplayName}] 启动战网客户端（不改变当前默认登录账号）。");

            string? exe = GetBattleNetPath();
            if (exe == null || !File.Exists(exe))
            {
                L("未能找到战网可执行文件。若使用便携版，请在【设置】里指定战网程序路径。");
                throw new FileNotFoundException(
                    "未能找到战网可执行文件。若使用便携版，请在【设置】里指定战网程序路径。",
                    exe ?? "Battle.net.exe");
            }

            string args = $"--setregion={regionInfo.Code} --setlanguage={regionInfo.DefaultLocale}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                };

                Process.Start(psi);
                L($"已以 [{regionInfo.DisplayName}] 启动战网客户端。");
                L($"  路径: {exe}");
                L($"  参数: {args}");
                L("请在客户端里登录该区服的账号（记得勾选“记住密码”）。");
            }
            catch (Exception ex)
            {
                L($"启动战网失败: {ex.Message}");
                throw;
            }

            return sb.ToString();
        }

        private static void StartBattleNet(string exePath, RegionInfo region, Action<string> log)
        {
            try
            {
                string args = $"--setregion={region.Code} --setlanguage={region.DefaultLocale}";

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };

                Process.Start(psi);
                log($"已以 [{region.DisplayName}] 重新启动战网客户端。");
                log($"  路径: {exePath}");
                log($"  参数: {args}");
            }
            catch (Exception ex)
            {
                log($"启动战网失败: {ex.Message}");
            }
        }

        private static void TrySetConfigRegion(JsonNode config, RegionInfo region, Action<string> log)
        {
            try
            {
                var clientNode = config["Client"];
                if (clientNode == null) return;

                var loginSettings = clientNode["LoginSettings"] as JsonObject;
                if (loginSettings == null)
                {
                    loginSettings = new JsonObject();
                    clientNode["LoginSettings"] = loginSettings;
                }

                loginSettings["AllowedRegions"] = "CN;US;EU;KR;TW";
                loginSettings["AllowedLocales"] =
                    "zhCN;deDE;enGB;enUS;esMX;esES;frFR;itIT;plPL;ptBR;ruRU;koKR;zhTW";
                loginSettings["SelectedRegion"] = region.Code;

                log($"已写入区域配置: {region.Code}");
            }
            catch (Exception ex)
            {
                log($"写入区域配置失败（不影响账号切换）: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭战网客户端：**优先让它正常退出**，只有超时才强杀。
        ///
        /// 为什么不能直接 Kill()：强杀时客户端来不及做退出流程
        /// （保存状态、把刷新后的令牌写回注册表、与服务端结束会话），
        /// 这会直接影响下次启动能否免密恢复 —— 表现为"切换后经常要重新登录"。
        ///
        /// 做法：对带主窗口的那个进程发 WM_CLOSE（CloseMainWindow），
        /// 等它自己退；主窗口进程退出后，其余后台进程（战网会拉起多个）随之结束，
        /// 仍未结束的才强杀。
        /// </summary>
        private static void CloseBattleNet(Action<string> log)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName("Battle.net");
            }
            catch (Exception ex)
            {
                log($"枚举战网进程失败: {ex.Message}");
                return;
            }

            if (procs.Length == 0)
            {
                log("战网未在运行。");
                return;
            }

            // 带主窗口的那个才是"主进程"，退出请求要发给它
            Process? main = null;
            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
                    {
                        main = p;
                        break;
                    }
                }
                catch { }
            }

            bool gracefulSent = false;
            if (main != null)
            {
                try
                {
                    gracefulSent = main.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    log($"发送退出请求失败: {ex.Message}");
                }
            }

            if (gracefulSent)
            {
                log("已请求战网正常退出，等待它自行收尾…");

                // 给客户端留出收尾时间：它要保存配置并刷新令牌
                const int waitMs = 8000;
                const int stepMs = 250;
                for (int waited = 0; waited < waitMs; waited += stepMs)
                {
                    try
                    {
                        if (main!.HasExited) break;
                    }
                    catch { break; }
                    Thread.Sleep(stepMs);
                }

                try
                {
                    if (main!.HasExited) log("战网已正常退出。");
                    else log($"战网在 {waitMs / 1000} 秒内没有退出，将强制结束。");
                }
                catch { }
            }
            else
            {
                // 找不到主窗口（可能已在退出中，或以别的方式启动），退回关闭进程
                log("未找到战网主窗口，无法请求正常退出。");
            }

            // 兜底：把仍在运行的（含后台进程）强杀掉，否则改配置会被占用
            foreach (var p in procs)
            {
                try
                {
                    if (p.HasExited) continue;

                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        // 主窗口可能已经在退出了（CloseMainWindow 返回 false），
                        // 那种情况别再等 3 秒
                        if (p.CloseMainWindow() && p.WaitForExit(3000)) continue;
                        if (p.HasExited) continue;
                    }

                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
            }

            bool anyAlive;
            try
            {
                anyAlive = Process.GetProcessesByName("Battle.net")
                                  .Any(p => { try { return !p.HasExited; } catch { return false; } });
            }
            catch { anyAlive = false; }

            if (!anyAlive) log("战网相关进程已全部结束。");

            Thread.Sleep(500);
        }

        // ------------------------------------------------------------
        //  查找战网可执行文件
        // ------------------------------------------------------------
        /// <summary>
        /// 战网 exe 路径。
        /// 1) 优先 AppSettings.BattleNetExePath（便携版用户手动指定）
        /// 2) 注册表卸载信息 InstallLocation / DisplayIcon
        /// 3) HKCU 战网自身配置 ClientPath
        /// </summary>
        public static string? GetBattleNetPath()
        {
            // 1. 用户配置
            var custom = AppSettings.Current.BattleNetExePath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                return custom;

            // 2. HKLM 卸载信息
            const string uninstallKey =
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(uninstallKey);
                if (key != null)
                {
                    if (key.GetValue("InstallLocation") is string installLocation &&
                        !string.IsNullOrEmpty(installLocation))
                    {
                        string exePath = Path.Combine(installLocation, "Battle.net.exe");
                        if (File.Exists(exePath)) return exePath;

                        string launcher = Path.Combine(installLocation, "Battle.net Launcher.exe");
                        if (File.Exists(launcher)) return launcher;
                    }

                    if (key.GetValue("DisplayIcon") is string displayIcon &&
                        !string.IsNullOrEmpty(displayIcon))
                    {
                        string cleaned = displayIcon.Trim('"');
                        int comma = cleaned.IndexOf(',');
                        if (comma > 0) cleaned = cleaned.Substring(0, comma);
                        if (File.Exists(cleaned)) return cleaned;
                    }
                }
            }
            catch { }

            // 3. HKCU 战网自身配置
            const string clientKey = @"Software\Blizzard Entertainment\Battle.net";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(clientKey);
                if (key?.GetValue("ClientPath") is string clientPath &&
                    !string.IsNullOrEmpty(clientPath) &&
                    File.Exists(clientPath))
                {
                    return clientPath;
                }
            }
            catch { }

            return null;
        }
    }
}
