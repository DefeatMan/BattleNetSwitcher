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
using BattleNetSwitcher.Core.Snapshots;
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
        public static string ConfigPath => GetConfigPath();

        public static string GetConfigPath()
        {
            var custom = AppSettings.Current.BattleNetConfigPath;
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battle.net", "Battle.net.config");
        }

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

        // ============================================================
        //  本地状态快照
        // ============================================================
        /// <summary>
        /// 保存当前战网客户端的本地状态为 (email, region) 的快照。
        /// 若 AppSettings.CloseBattleNetBeforeSave == true，会先优雅关闭战网。
        /// 切换流程内部已经关过战网，可传 closeBattleNet:false 跳过重复关闭。
        /// </summary>
        public static SnapshotInfo SaveSnapshot(
            string email,
            string region,
            Action<string>? log = null,
            bool closeBattleNet = true)
        {
            void L(string m) => log?.Invoke(m);

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("邮箱不能为空。", nameof(email));

            if (closeBattleNet && AppSettings.Current.CloseBattleNetBeforeSave)
            {
                CloseBattleNet(L);
            }

            string configPath = GetConfigPath();
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    "未找到 Battle.net.config 文件，无法保存快照。" +
                    "请确认战网已安装并登录过该账号。", configPath);

            var info = SnapshotManager.Save(email, region, log);
            L($"快照保存完成：{info.Email} [{info.Region}]，" +
              $"{info.FileCount} 个文件，{info.UniqueIdCount} 个 UnifiedAuth 条目。");
            return info;
        }

        /// <summary>
        /// 恢复指定 (email, region) 的快照（不启动战网）。失败不抛异常，只返回 false。
        /// </summary>
        public static bool TryRestoreSnapshot(
            string email, string region, Action<string>? log = null)
        {
            void L(string m) => log?.Invoke(m);

            if (string.IsNullOrWhiteSpace(email)) return false;

            try
            {
                if (!SnapshotManager.Exists(email, region))
                {
                    L($"未找到 {email} [{region}] 的本地快照，跳过恢复（将回退到常规切换流程）。");
                    return false;
                }

                SnapshotManager.Restore(email, region, log);
                L($"已恢复 {email} [{region}] 的本地快照。");
                return true;
            }
            catch (Exception ex)
            {
                L($"恢复快照失败（继续常规流程）：{ex.Message}");
                return false;
            }
        }

        // ============================================================
        //  切换账号
        //
        //  执行顺序：
        //    1. 优雅关闭战网（让客户端把最新凭证/配置写盘）
        //    2. 读 (currentEmail, currentRegion)：
        //       若与目标 (email, region) 不完全相同 → 自动更新 current 的快照
        //    3. 恢复目标 (email, region) 的快照（若存在）
        //    4. 重写 Battle.net.config（SavedAccountNames / SelectedRegion）
        //    5. 以 --setregion 启动战网
        // ============================================================
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

            // ============ 1. 关闭战网 ============
            CloseBattleNet(L);

            // ============ 2. 决定是否需要为"源账号"更新快照 ============
            string? currentEmail = null;
            string currentRegion = regionInfo.Code;
            try
            {
                var (saved, sel) = ReadConfigAccountsAndRegion();
                if (saved.Count > 0) currentEmail = saved[0];
                if (!string.IsNullOrWhiteSpace(sel)) currentRegion = sel.ToUpperInvariant();
            }
            catch { }

            bool sameTarget = !string.IsNullOrEmpty(currentEmail) &&
                              currentEmail.Equals(targetEmail, StringComparison.OrdinalIgnoreCase) &&
                              currentRegion.Equals(regionInfo.Code, StringComparison.OrdinalIgnoreCase);

            if (AppSettings.Current.UseSnapshotOnSwitch)
            {
                if (!string.IsNullOrEmpty(currentEmail) && !sameTarget)
                {
                    L($"检测到切换前登录的账号 {currentEmail} [{currentRegion}]，" +
                      "正在自动更新其快照…");
                    try
                    {
                        // 战网已关，跳过内部再次关闭
                        SaveSnapshot(currentEmail, currentRegion, L, closeBattleNet: false);
                        L($"已更新 {currentEmail} [{currentRegion}] 的快照（保存下线前最新状态）。");
                    }
                    catch (Exception ex)
                    {
                        L($"自动更新当前账号快照失败（不影响切换）：{ex.Message}");
                    }
                }
                else if (sameTarget)
                {
                    L($"当前登录账号与目标一致（{currentEmail} [{currentRegion}]），无需更新快照。");
                }
            }

            // ============ 3. 恢复目标快照 ============
            bool snapshotRestored = false;
            if (AppSettings.Current.UseSnapshotOnSwitch && !sameTarget)
            {
                snapshotRestored = TryRestoreSnapshot(targetEmail, regionInfo.Code, L);
            }

            // 快照恢复会覆盖 Battle.net.config，需要重新读一次
            if (snapshotRestored)
            {
                try
                {
                    var again = LoadAccounts();
                    if (again.Count > 0) accounts = again;
                }
                catch { }

                if (!accounts.Any(a => a.Equals(targetEmail, StringComparison.OrdinalIgnoreCase)))
                    accounts.Insert(0, targetEmail);
            }

            // ============ 4. 重写 Battle.net.config ============
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

            // ============ 5. 重启战网 ============
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

            // ---- 收尾说明 ----
            if (sameTarget)
            {
                L("当前账号与目标一致，已跳过快照操作。");
            }
            else if (snapshotRestored)
            {
                L("已从本地快照恢复登录状态，理论上客户端会直接向服务端续期，无需浏览器验证。");
                L("若仍被要求重新登录：说明该快照的令牌已被服务端作废，" +
                  "请在客户端里重新登录一次并刷新快照。");
            }
            else
            {
                L("未找到该账号在该区服的快照，走常规流程。");
                L("切换完成后，可在客户端里登录一次并点“更新当前快照”保存一份。");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 直接以指定区服启动战网客户端（不改动默认登录账号）。
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
        /// 关闭战网客户端：优先让它正常退出，只有超时才强杀。
        /// </summary>
        internal static void CloseBattleNet(Action<string> log)
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
                log("未找到战网主窗口，无法请求正常退出。");
            }

            foreach (var p in procs)
            {
                try
                {
                    if (p.HasExited) continue;

                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
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
        public static string? GetBattleNetPath()
        {
            var custom = AppSettings.Current.BattleNetExePath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                return custom;

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
