#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using BattleNetSwitcher.Core;

namespace BattleNetSwitcher.Cli
{
    [SupportedOSPlatform("windows")]
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            try { AccountBook.EnsureInitialized(); } catch { }

            string cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "list":
                case "-l":
                case "--list":
                    return ListAccounts(GetOption(args, "--region", "-r"));

                case "regions":
                    return ListRegions();

                case "switch":
                case "-s":
                case "--switch":
                    {
                        var email = GetPositional(args, 1, "--region", "-r");
                        if (string.IsNullOrEmpty(email))
                        {
                            Console.Error.WriteLine("错误：缺少邮箱参数。");
                            return 1;
                        }
                        return Switch(email, GetOption(args, "--region", "-r"));
                    }

                case "add":
                    {
                        var email = GetPositional(args, 1, "--region", "-r");
                        var region = GetOption(args, "--region", "-r");
                        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(region))
                        {
                            Console.Error.WriteLine("用法：add <邮箱> --region <区服>");
                            return 1;
                        }
                        return AddAccount(email, region);
                    }

                case "remove":
                case "rm":
                    {
                        var email = GetPositional(args, 1, "--region", "-r");
                        var region = GetOption(args, "--region", "-r");
                        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(region))
                        {
                            Console.Error.WriteLine("用法：remove <邮箱> --region <区服>");
                            return 1;
                        }
                        return RemoveAccount(email, region);
                    }

                case "snapshot":
                case "snap":
                    return HandleSnapshot(args);

                case "version":
                case "-v":
                case "--version":
                    PrintVersion();
                    return 0;

                case "help":
                case "-h":
                case "--help":
                case "/?":
                    PrintHelp();
                    return 0;

                default:
                    if (!args[0].StartsWith("-", StringComparison.Ordinal))
                    {
                        return Switch(args[0], GetOption(args, "--region", "-r"));
                    }
                    Console.Error.WriteLine($"未知命令：{args[0]}");
                    Console.Error.WriteLine();
                    PrintHelp();
                    return 1;
            }
        }

        // ------------------------------------------------------------
        //  参数解析
        // ------------------------------------------------------------
        private static string? GetOption(string[] args, params string[] names)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];

                foreach (var name in names)
                {
                    if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                        return a.Substring(name.Length + 1);

                    if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length)
                        return args[i + 1];
                }
            }
            return null;
        }

        private static string? GetPositional(string[] args, int start, params string[] optionNames)
        {
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("-", StringComparison.Ordinal)) continue;

                bool isOptionValue = false;
                for (int j = 0; j < i; j++)
                {
                    foreach (var name in optionNames)
                    {
                        if (string.Equals(args[j], name, StringComparison.OrdinalIgnoreCase) &&
                            j + 1 == i)
                        {
                            isOptionValue = true;
                            break;
                        }
                    }
                    if (isOptionValue) break;
                }

                if (isOptionValue) continue;

                return a;
            }
            return null;
        }

        // ------------------------------------------------------------
        //  list / regions
        // ------------------------------------------------------------
        private static int ListAccounts(string? regionFilter)
        {
            try
            {
                var (saved, selectedRegion) = AccountSwitcher.ReadConfigAccountsAndRegion();
                string? currentFirst = saved.Count > 0 ? saved[0] : null;

                var regionsToShow = string.IsNullOrEmpty(regionFilter)
                    ? AccountBook.GetActiveRegions()
                    : AccountBook.GetActiveRegions()
                        .Where(r => string.Equals(r, regionFilter,
                                                  StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (regionsToShow.Count == 0)
                {
                    if (string.IsNullOrEmpty(regionFilter))
                        Console.WriteLine("没有已保存的账号。");
                    else
                        Console.WriteLine($"区服 {regionFilter} 下没有账号。");
                    return 0;
                }

                foreach (var regionCode in regionsToShow)
                {
                    var info = RegionInfo.TryFromCode(regionCode);
                    string title = info?.DisplayName ?? regionCode;

                    Console.WriteLine($"[{title}]");

                    var emails = AccountBook.GetEmailsForRegion(regionCode);
                    foreach (var email in emails)
                    {
                        bool isCurrent =
                            !string.IsNullOrEmpty(currentFirst) &&
                            string.Equals(email, currentFirst, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(regionCode, selectedRegion,
                                          StringComparison.OrdinalIgnoreCase);

                        bool hasSnapshot = false;
                        try { hasSnapshot = SnapshotManager.Exists(email, regionCode); } catch { }

                        string mark = isCurrent ? "  <-- 当前" : "";
                        string snap = hasSnapshot ? "  [快照]" : "";
                        Console.WriteLine($"  {email}{mark}{snap}");
                    }
                    Console.WriteLine();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("读取失败：" + ex.Message);
                return 1;
            }
        }

        private static int ListRegions()
        {
            try
            {
                var regions = AccountBook.GetActiveRegions();

                if (regions.Count == 0)
                {
                    Console.WriteLine("没有任何有账号的区服。");
                    return 0;
                }

                Console.WriteLine("有账号的区服：");
                foreach (var code in regions)
                {
                    var info = RegionInfo.TryFromCode(code);
                    int count = AccountBook.GetEmailsForRegion(code).Count;
                    Console.WriteLine($"  {code}  {info?.DisplayName ?? ""}  ({count} 个账号)");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("读取失败：" + ex.Message);
                return 1;
            }
        }

        // ------------------------------------------------------------
        //  switch
        // ------------------------------------------------------------
        private static int Switch(string email, string? region)
        {
            try
            {
                string targetCode;

                if (!string.IsNullOrEmpty(region))
                {
                    var info = RegionInfo.TryFromCode(region);
                    if (info == null)
                    {
                        Console.Error.WriteLine(
                            $"错误：未知区服 '{region}'。可用：{string.Join(", ", RegionInfo.All.Select(r => r.Code))}");
                        return 1;
                    }
                    targetCode = info.Code;
                }
                else
                {
                    var regions = AccountBook.GetRegionsForEmail(email);

                    if (regions.Count == 0)
                    {
                        Console.Error.WriteLine(
                            $"错误：本地未记录账号 {email} 的区服。\r\n" +
                            $"请用 --region 指定，或先执行：add {email} --region <区服>");
                        return 1;
                    }

                    if (regions.Count > 1)
                    {
                        Console.Error.WriteLine(
                            $"错误：账号 {email} 在多个区服有记录，请用 --region 指定：\r\n" +
                            "  " + string.Join(", ", regions));
                        return 1;
                    }

                    targetCode = regions[0];
                }

                var targetInfo = RegionInfo.FromCode(targetCode);

                AccountSwitcher.SwitchAccount(
                    email,
                    region: targetInfo.Region,
                    restart: true,
                    log: Console.WriteLine);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("切换失败：" + ex.Message);
                return 1;
            }
        }

        // ------------------------------------------------------------
        //  add / remove
        // ------------------------------------------------------------
        private static int AddAccount(string email, string region)
        {
            try
            {
                var info = RegionInfo.TryFromCode(region);
                if (info == null)
                {
                    Console.Error.WriteLine(
                        $"错误：未知区服 '{region}'。可用：{string.Join(", ", RegionInfo.All.Select(r => r.Code))}");
                    return 1;
                }

                try
                {
                    var saved = AccountSwitcher.LoadAccounts();
                    bool exists = saved.Any(s =>
                        string.Equals(s, email, StringComparison.OrdinalIgnoreCase));

                    if (!exists)
                    {
                        Console.Error.WriteLine(
                            $"错误：邮箱 {email} 未在战网中保存。\r\n" +
                            "请先在战网客户端里用该账号登录一次并勾选“记住密码”。");
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("读取战网配置失败：" + ex.Message);
                    return 1;
                }

                if (AccountBook.Add(email, info.Code))
                {
                    Console.WriteLine($"已将 {email} 添加到 [{info.DisplayName}]。");
                    return 0;
                }

                Console.WriteLine($"{email} 已经在 [{info.DisplayName}] 中。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("添加失败：" + ex.Message);
                return 1;
            }
        }

        private static int RemoveAccount(string email, string region)
        {
            try
            {
                var info = RegionInfo.TryFromCode(region);
                if (info == null)
                {
                    Console.Error.WriteLine($"错误：未知区服 '{region}'。");
                    return 1;
                }

                if (AccountBook.Remove(email, info.Code))
                {
                    Console.WriteLine($"已从 [{info.DisplayName}] 移除 {email}。");
                    return 0;
                }

                Console.WriteLine($"{email} 不在 [{info.DisplayName}] 中。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("移除失败：" + ex.Message);
                return 1;
            }
        }

        // ------------------------------------------------------------
        //  snapshot 子命令
        // ------------------------------------------------------------
        private static int HandleSnapshot(string[] args)
        {
            if (args.Length < 2)
            {
                PrintSnapshotHelp();
                return 1;
            }

            string sub = args[1].ToLowerInvariant();

            switch (sub)
            {
                case "save":
                    {
                        var email = GetPositional(args, 2, "--region", "-r");
                        if (string.IsNullOrEmpty(email))
                        {
                            Console.Error.WriteLine("用法：snapshot save <邮箱> [--region <区服>]");
                            return 1;
                        }

                        string? regionOpt = GetOption(args, "--region", "-r");
                        string? regionCode = ResolveRegion(email, regionOpt);
                        if (regionCode == null) return 1;

                        return SnapshotSave(email, regionCode);
                    }

                case "list":
                case "ls":
                    return SnapshotList(GetOption(args, "--region", "-r"));

                case "remove":
                case "rm":
                case "delete":
                    {
                        var email = GetPositional(args, 2, "--region", "-r");
                        if (string.IsNullOrEmpty(email))
                        {
                            Console.Error.WriteLine(
                                "用法：snapshot remove <邮箱> [--region <区服>]\r\n" +
                                "  指定 --region 只删该区服的快照；省略则删除该邮箱所有区服的快照。");
                            return 1;
                        }
                        return SnapshotRemove(email, GetOption(args, "--region", "-r"));
                    }

                case "restore":
                    {
                        var email = GetPositional(args, 2, "--region", "-r");
                        if (string.IsNullOrEmpty(email))
                        {
                            Console.Error.WriteLine(
                                "用法：snapshot restore <邮箱> [--region <区服>]");
                            return 1;
                        }

                        string? regionOpt = GetOption(args, "--region", "-r");
                        string? regionCode = ResolveRegion(email, regionOpt);
                        if (regionCode == null) return 1;

                        return SnapshotRestore(email, regionCode);
                    }

                case "help":
                case "-h":
                case "--help":
                    PrintSnapshotHelp();
                    return 0;

                default:
                    Console.Error.WriteLine($"未知 snapshot 子命令：{sub}");
                    Console.Error.WriteLine();
                    PrintSnapshotHelp();
                    return 1;
            }
        }

        /// <summary>
        /// 解析区服：显式 --region &gt; 账号本唯一记录 &gt; 配置文件当前区服。
        /// 无法唯一确定时返回 null。
        /// </summary>
        private static string? ResolveRegion(string email, string? regionOpt)
        {
            if (!string.IsNullOrEmpty(regionOpt))
            {
                var info = RegionInfo.TryFromCode(regionOpt);
                if (info == null)
                {
                    Console.Error.WriteLine(
                        $"错误：未知区服 '{regionOpt}'。可用：{string.Join(", ", RegionInfo.All.Select(r => r.Code))}");
                    return null;
                }
                return info.Code;
            }

            var regions = AccountBook.GetRegionsForEmail(email);
            if (regions.Count == 1) return regions[0];
            if (regions.Count > 1)
            {
                Console.Error.WriteLine(
                    $"错误：账号 {email} 在多个区服有记录，请用 --region 指定：\r\n" +
                    "  " + string.Join(", ", regions));
                return null;
            }

            // 落回配置
            try
            {
                var (_, selected) = AccountSwitcher.ReadConfigAccountsAndRegion();
                return string.IsNullOrEmpty(selected) ? "CN" : selected;
            }
            catch
            {
                return "CN";
            }
        }

        private static int SnapshotSave(string email, string regionCode)
        {
            try
            {
                bool exists = SnapshotManager.Exists(email, regionCode);
                Console.WriteLine(exists
                    ? $"更新已有快照：{email} [{regionCode}]"
                    : $"保存新快照：{email} [{regionCode}]");
                Console.WriteLine("（保存前会先关闭战网客户端）");
                Console.WriteLine();

                var info = AccountSwitcher.SaveSnapshot(email, regionCode, Console.WriteLine);

                Console.WriteLine();
                Console.WriteLine("快照已保存：");
                Console.WriteLine($"  邮箱：{info.Email}");
                Console.WriteLine($"  区服：{info.Region}");
                Console.WriteLine($"  文件：{info.FileCount} 个");
                Console.WriteLine($"  UnifiedAuth 条目：{info.UniqueIdCount} 个");
                Console.WriteLine($"  目录：{info.DirectoryPath}");
                Console.WriteLine($"  时间：{info.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("保存快照失败：" + ex.Message);
                return 1;
            }
        }

        private static int SnapshotList(string? regionFilter)
        {
            try
            {
                var list = SnapshotManager.List();

                if (!string.IsNullOrEmpty(regionFilter))
                {
                    var info = RegionInfo.TryFromCode(regionFilter);
                    if (info == null)
                    {
                        Console.Error.WriteLine($"错误：未知区服 '{regionFilter}'。");
                        return 1;
                    }
                    list = list
                        .Where(s => string.Equals(s.Region, info.Code,
                                                  StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (list.Count == 0)
                {
                    Console.WriteLine("没有已保存的快照。");
                    Console.WriteLine("用 `snapshot save <邮箱> [--region <区服>]` 保存一个。");
                    return 0;
                }

                Console.WriteLine($"共 {list.Count} 个快照：");
                Console.WriteLine();

                foreach (var s in list)
                {
                    Console.WriteLine($"  {s.Email}  [{s.Region}]");
                    Console.WriteLine($"    文件：{s.FileCount} 个，UnifiedAuth：{s.UniqueIdCount} 个");
                    Console.WriteLine($"    更新：{s.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"    目录：{s.DirectoryPath}");
                    Console.WriteLine();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("读取快照列表失败：" + ex.Message);
                return 1;
            }
        }

        private static int SnapshotRemove(string email, string? regionOpt)
        {
            try
            {
                if (!string.IsNullOrEmpty(regionOpt))
                {
                    var info = RegionInfo.TryFromCode(regionOpt);
                    if (info == null)
                    {
                        Console.Error.WriteLine($"错误：未知区服 '{regionOpt}'。");
                        return 1;
                    }

                    if (SnapshotManager.Remove(email, info.Code))
                    {
                        Console.WriteLine($"已删除 {email} [{info.Code}] 的快照。");
                        return 0;
                    }

                    Console.WriteLine($"{email} 没有 [{info.Code}] 区服的快照。");
                    return 0;
                }

                int removed = SnapshotManager.RemoveAll(email);
                if (removed == 0)
                {
                    Console.WriteLine($"{email} 没有已保存的快照。");
                    return 0;
                }

                Console.WriteLine($"已删除 {email} 的 {removed} 个快照（所有区服）。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("删除快照失败：" + ex.Message);
                return 1;
            }
        }

        private static int SnapshotRestore(string email, string regionCode)
        {
            try
            {
                if (!SnapshotManager.Exists(email, regionCode))
                {
                    Console.Error.WriteLine($"{email} [{regionCode}] 没有已保存的快照。");
                    return 1;
                }

                Console.WriteLine($"将恢复 {email} [{regionCode}] 的快照。");
                Console.WriteLine("如果战网客户端正在运行，请先手动关闭。");
                Console.WriteLine();

                if (AccountSwitcher.TryRestoreSnapshot(email, regionCode, Console.WriteLine))
                {
                    Console.WriteLine();
                    Console.WriteLine("恢复完成。下次启动战网客户端时将使用该快照。");
                    return 0;
                }

                Console.Error.WriteLine("恢复失败。");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("恢复快照失败：" + ex.Message);
                return 1;
            }
        }

        // ------------------------------------------------------------
        //  version / help
        // ------------------------------------------------------------
        private static void PrintVersion()
        {
            Console.WriteLine($"BattleNetSwitcher CLI v{AppInfo.Version}");
            Console.WriteLine("  Windows 10/11 x64");
            if (!string.IsNullOrEmpty(AppInfo.RepositoryUrl))
                Console.WriteLine($"  {AppInfo.RepositoryUrl}");
        }

        private static void PrintHelp()
        {
            Console.WriteLine($"BattleNetSwitcher CLI v{AppInfo.Version}");
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  BattleNetSwitcher.Cli list [--region <区服>]");
            Console.WriteLine("                                列出所有（或指定区服的）账号");
            Console.WriteLine("  BattleNetSwitcher.Cli regions 列出所有有账号的区服");
            Console.WriteLine("  BattleNetSwitcher.Cli switch <邮箱> [--region <区服>]");
            Console.WriteLine("                                切换账号（若邮箱在多个区服有记录，必须指定区服）");
            Console.WriteLine("  BattleNetSwitcher.Cli <邮箱> [--region <区服>]");
            Console.WriteLine("                                同上（简写）");
            Console.WriteLine("  BattleNetSwitcher.Cli add <邮箱> --region <区服>");
            Console.WriteLine("                                添加账号到区服");
            Console.WriteLine("  BattleNetSwitcher.Cli remove <邮箱> --region <区服>");
            Console.WriteLine("                                从区服移除账号");
            Console.WriteLine();
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot save <邮箱> [--region <区服>]");
            Console.WriteLine("                                保存/更新账号在指定区服的本地状态快照");
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot list [--region <区服>]");
            Console.WriteLine("                                列出所有（或指定区服的）快照");
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot remove <邮箱> [--region <区服>]");
            Console.WriteLine("                                删除快照（不指定区服 = 删除所有区服）");
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot restore <邮箱> [--region <区服>]");
            Console.WriteLine("                                恢复指定区服的快照（需先手动关闭战网）");
            Console.WriteLine();
            Console.WriteLine("  BattleNetSwitcher.Cli version 显示版本号");
            Console.WriteLine("  BattleNetSwitcher.Cli help    显示本帮助");
            Console.WriteLine();
            Console.WriteLine($"区服代码：{string.Join(", ", RegionInfo.All.Select(r => r.Code))}");
            Console.WriteLine("  --region / -r   指定区服");
            Console.WriteLine();
            Console.WriteLine("示例：");
            Console.WriteLine("  BattleNetSwitcher.Cli list");
            Console.WriteLine("  BattleNetSwitcher.Cli list --region US");
            Console.WriteLine("  BattleNetSwitcher.Cli switch a@b.com --region US");
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot save a@b.com --region US");
            Console.WriteLine("  BattleNetSwitcher.Cli snapshot list");
        }

        private static void PrintSnapshotHelp()
        {
            Console.WriteLine("snapshot 子命令：");
            Console.WriteLine();
            Console.WriteLine("  snapshot save <邮箱> [--region <区服>]");
            Console.WriteLine("      保存或更新指定邮箱在指定区服的本地状态快照。");
            Console.WriteLine("      保存前会先关闭战网客户端；请确保当前已登录该账号并完成验证。");
            Console.WriteLine("      若 --region 省略，将尝试从账号本或配置推断。");
            Console.WriteLine();
            Console.WriteLine("  snapshot list [--region <区服>]");
            Console.WriteLine("      列出所有（或指定区服的）快照。");
            Console.WriteLine();
            Console.WriteLine("  snapshot remove <邮箱> [--region <区服>]");
            Console.WriteLine("      删除指定邮箱的快照。");
            Console.WriteLine("      指定 --region 只删该区服的；省略则删除该邮箱所有区服的快照。");
            Console.WriteLine();
            Console.WriteLine("  snapshot restore <邮箱> [--region <区服>]");
            Console.WriteLine("      把指定区服的快照覆盖回战网的本地位置。");
            Console.WriteLine("      请先手动关闭战网客户端；恢复完成后下次启动战网时生效。");
        }
    }
}
