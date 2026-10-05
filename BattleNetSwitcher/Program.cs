#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows.Forms;
using BattleNetSwitcher.Core;
using BattleNetSwitcher.Forms;

namespace BattleNetSwitcher
{
    /// <summary>
    /// GUI 入口。
    /// 若 AppSettings.DisablePullout == true，跳过 UAC 提权。
    /// 若用户拒绝 UAC，则自动把 DisablePullout 写为 true 并继续以普通用户启动。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class Program
    {
        /// <summary>
        /// 本次运行是否因用户拒绝 UAC 而自动禁用了拔线。
        /// MainForm 据此显示一次提示。
        /// </summary>
        public static bool AutoDisabledPulloutDueToUacRefusal { get; private set; }

        [STAThread]
        private static void Main()
        {
            bool needAdmin = !AppSettings.Current.DisablePullout;

            if (needAdmin && !IsAdministrator())
            {
                if (TryRelaunchAsAdmin())
                    return;   // 提权成功，新进程接管；当前进程退出

                // ---------- 用户拒绝了 UAC ----------
                // 1. 写入配置：禁用拔线
                // 2. 以普通用户身份启动 UI（不再尝试提权）
                AutoDisabledPulloutDueToUacRefusal = true;

                try
                {
                    AppSettings.Current.DisablePullout = true;
                    AppSettings.Save();
                    AppSettings.Reload();
                }
                catch
                {
                    // 配置写入失败也不阻塞启动
                }
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        private static bool TryRelaunchAsAdmin()
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 1223 = 用户在 UAC 对话框点了“否”
                // 其他 Win32Exception 也一并当作“无法提权”
                return false;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
