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

            // 首次初始化本地账号本
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
                    // 单个非选项参数视为 email（简写 switch）
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
        /// <summary>支持 --region CN / --region=CN / -r CN / -r=CN。</summary>
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

        /// <summary>找出第一个非选项参数，跳过选项和它们的值。</summary>
        private static string? GetPositional(string[] args, int start, params string[] optionNames)
        {
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("-", StringComparison.Ordinal)) continue;

                // 是否为某选项的值
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
        //  list
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

                        Console.WriteLine($"  {email}{(isCurrent ? "  <-- 当前" : "")}");
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

        // ------------------------------------------------------------
        //  regions
        // ------------------------------------------------------------
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
                // 确定目标区服
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
        //  add
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

                // 校验邮箱已在 SavedAccountNames 里
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

        // ------------------------------------------------------------
        //  remove
        // ------------------------------------------------------------
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
            Console.WriteLine("  BattleNetSwitcher.Cli add a@b.com --region KR");
        }
    }
}
