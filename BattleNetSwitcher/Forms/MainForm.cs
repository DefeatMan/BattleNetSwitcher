#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BattleNetSwitcher.Core;

namespace BattleNetSwitcher.Forms
{
    [SupportedOSPlatform("windows")]
    internal sealed class MainForm : Form
    {
        private readonly TabControl _tabs;
        private readonly AccountPanel _accountPanel;
        private readonly NetworkPanel _networkPanel;
        private readonly SettingsPanel _settingsPanel;
        private readonly TabPage _tabNetwork;

        private readonly StatusStrip _status;
        private readonly ToolStripStatusLabel _lblMode;

        public MainForm()
        {
            Text = "战网账号切换 & 一键拔线";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 560);
            MinimumSize = new Size(620, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            Icon = LoadAppIcon();
            ShowIcon = true;

            // ---- 三个 Tab ----
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6)
            };

            _accountPanel = new AccountPanel { Dock = DockStyle.Fill };
            var tabAccount = new TabPage("账号切换") { UseVisualStyleBackColor = true };
            tabAccount.Controls.Add(_accountPanel);

            _networkPanel = new NetworkPanel { Dock = DockStyle.Fill };
            _tabNetwork = new TabPage("一键拔线") { UseVisualStyleBackColor = true };
            _tabNetwork.Controls.Add(_networkPanel);

            _settingsPanel = new SettingsPanel { Dock = DockStyle.Fill };
            _settingsPanel.Saved += OnSettingsSaved;
            var tabSettings = new TabPage("设置") { UseVisualStyleBackColor = true };
            tabSettings.Controls.Add(_settingsPanel);

            _tabs.TabPages.Add(tabAccount);
            _tabs.TabPages.Add(_tabNetwork);
            _tabs.TabPages.Add(tabSettings);

            // ---- 状态栏 ----
            _status = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                BackColor = SystemColors.Control
            };

            bool hasRepo = !string.IsNullOrEmpty(AppInfo.RepositoryUrl);

            var lblVersion = new ToolStripStatusLabel
            {
                Text = $"v{AppInfo.Version}",
                IsLink = hasRepo,
                LinkBehavior = LinkBehavior.HoverUnderline,
                ToolTipText = hasRepo
                    ? "点击查看 GitHub 发布页：" + AppInfo.ReleasesUrl
                    : "未配置 RepositoryUrl",
                ForeColor = SystemColors.GrayText
            };
            if (hasRepo)
                lblVersion.Click += (_, _) => OpenReleasesPage();

            var spacer = new ToolStripStatusLabel
            {
                Spring = true,
                Text = string.Empty
            };

            _lblMode = new ToolStripStatusLabel
            {
                Text = "",
                Spring = false
            };

            _status.Items.Add(lblVersion);
            _status.Items.Add(spacer);
            _status.Items.Add(_lblMode);

            Controls.Add(_tabs);
            Controls.Add(_status);

            ApplyPulloutState();
        }

        // ------------------------------------------------------------
        //  首次显示：若因 UAC 被拒而自动禁用了拔线，给一次提示
        // ------------------------------------------------------------
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (Program.AutoDisabledPulloutDueToUacRefusal)
            {
                MessageBox.Show(this,
                    "您取消了管理员权限请求。\r\n\r\n" +
                    "“一键拔线”功能已自动禁用，程序将以普通用户模式运行。\r\n" +
                    "如需启用，请到【设置】页取消勾选，重启后再次授权即可。",
                    "已禁用一键拔线",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ------------------------------------------------------------
        //  设置保存后：刷新“一键拔线”Tab 的可用性
        // ------------------------------------------------------------
        private void OnSettingsSaved(object? sender, bool needRestart)
        {
            ApplyPulloutState();

            if (needRestart)
            {
                var r = MessageBox.Show(this,
                    "禁用“一键拔线”状态的变更需要重启程序才能生效。\r\n是否现在重启？",
                    "需要重启",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (r == DialogResult.Yes)
                    RestartSelf();
            }
        }

        private void ApplyPulloutState()
        {
            bool disabled = AppSettings.Current.DisablePullout;

            if (disabled)
            {
                _networkPanel.Enabled = false;
                _tabNetwork.Text = "一键拔线（已禁用）";
                _tabNetwork.ForeColor = SystemColors.GrayText;
            }
            else
            {
                _networkPanel.Enabled = true;
                _tabNetwork.Text = "一键拔线";
                _tabNetwork.ForeColor = SystemColors.ControlText;
            }

            if (disabled)
            {
                _lblMode.Text = "已禁用拔线";
                _lblMode.ForeColor = SystemColors.GrayText;
            }
            else if (IsAdministrator())
            {
                _lblMode.Text = "管理员模式";
                _lblMode.ForeColor = Color.FromArgb(0, 128, 64);
            }
            else
            {
                _lblMode.Text = "普通用户";
                _lblMode.ForeColor = Color.FromArgb(180, 80, 0);
            }
        }

        private void RestartSelf()
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "重启失败：" + ex.Message,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------
        private void OpenReleasesPage()
        {
            string url = AppInfo.ReleasesUrl;
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "无法打开浏览器：" + ex.Message + Environment.NewLine + url,
                    "打开链接失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static Icon? LoadAppIcon()
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exe);
                    if (extracted != null) return extracted;
                }
            }
            catch { }

            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(icoPath)) return new Icon(icoPath);
            }
            catch { }

            return SystemIcons.Application;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _networkPanel.CleanupRules(); }
            catch { }

            base.OnFormClosed(e);
        }
    }
}
