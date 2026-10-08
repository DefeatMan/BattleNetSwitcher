#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BattleNetSwitcher.Core;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 设置页：战网路径 + 快照配置 + 禁用拔线。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SettingsPanel : UserControl
    {
        private TextBox _txtExe = null!;
        private TextBox _txtConfig = null!;
        private TextBox _txtSnapshotRoot = null!;
        private CheckBox _chkDisablePullout = null!;
        private CheckBox _chkUseSnapshot = null!;
        private CheckBox _chkCloseBeforeSave = null!;
        private Button _btnSave = null!;
        private Label _lblStatus = null!;

        /// <summary>保存后触发，参数：是否建议重启。</summary>
        public event EventHandler<bool>? Saved;

        public SettingsPanel()
        {
            BuildUi();
            LoadFromSettings();
        }

        // ------------------------------------------------------------
        //  界面
        // ------------------------------------------------------------
        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12),
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ---------- 控件区 ----------
            var content = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            // ---- 战网程序路径 ----
            var lblExe = new Label
            {
                Text = "战网程序路径：",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var exePanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Width = 640,
                Height = 32,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(0)
            };
            exePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            exePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            exePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _txtExe = new TextBox
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(0, 0, 6, 0)
            };

            var btnBrowseExe = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            btnBrowseExe.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "战网程序 (Battle.net*.exe)|Battle.net*.exe|可执行文件 (*.exe)|*.exe",
                    Title = "选择战网可执行文件"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtExe.Text = dlg.FileName;
            };

            exePanel.Controls.Add(_txtExe, 0, 0);
            exePanel.Controls.Add(btnBrowseExe, 1, 0);

            // ---- 配置文件路径 ----
            var lblCfg = new Label
            {
                Text = "配置文件路径：",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var cfgPanel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Width = 640,
                Height = 32,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            cfgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cfgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            cfgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            cfgPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _txtConfig = new TextBox
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(0, 0, 6, 0)
            };

            var btnBrowseCfg = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnBrowseCfg.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "战网配置 (Battle.net.config)|Battle.net.config|所有文件 (*.*)|*.*",
                    Title = "选择 Battle.net.config"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtConfig.Text = dlg.FileName;
            };

            var btnResetCfg = new Button
            {
                Text = "恢复默认",
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            btnResetCfg.Click += (_, _) => { _txtConfig.Text = ""; };

            cfgPanel.Controls.Add(_txtConfig, 0, 0);
            cfgPanel.Controls.Add(btnBrowseCfg, 1, 0);
            cfgPanel.Controls.Add(btnResetCfg, 2, 0);

            var lblHint = new Label
            {
                Text = "留空 = 使用默认位置。便携版 / 绿色版战网请手动指定。",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 16)
            };

            // ---- 本地快照区块 ----
            var lblSnapshot = new Label
            {
                Text = "本地状态快照（保存登录凭证，减少切换时的浏览器验证）：",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4)
            };

            _chkUseSnapshot = new CheckBox
            {
                Text = "切换账号时优先恢复本地快照（推荐）",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            _chkCloseBeforeSave = new CheckBox
            {
                Text = "保存快照前自动关闭战网客户端（推荐）",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            var lblSnapRoot = new Label
            {
                Text = "快照根目录（留空 = 默认）：",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var snapRootPanel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Width = 640,
                Height = 32,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            snapRootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            snapRootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            snapRootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            snapRootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _txtSnapshotRoot = new TextBox
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(0, 0, 6, 0)
            };

            var btnBrowseSnap = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnBrowseSnap.Click += (_, _) =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "选择快照根目录",
                    ShowNewFolderButton = true
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtSnapshotRoot.Text = dlg.SelectedPath;
            };

            var btnResetSnap = new Button
            {
                Text = "恢复默认",
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            btnResetSnap.Click += (_, _) => { _txtSnapshotRoot.Text = ""; };

            snapRootPanel.Controls.Add(_txtSnapshotRoot, 0, 0);
            snapRootPanel.Controls.Add(btnBrowseSnap, 1, 0);
            snapRootPanel.Controls.Add(btnResetSnap, 2, 0);

            var lblSnapHint = new Label
            {
                Text = "默认：%APPDATA%\\BattleNetSwitcher\\Snapshots\\<邮箱>\\",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 16)
            };

            // ---- 禁用拔线 ----
            _chkDisablePullout = new CheckBox
            {
                Text = "禁用“一键拔线”功能（GUI 启动不再请求管理员权限）",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var lblRestart = new Label
            {
                Text = "※ 修改后需要重启程序才能完全生效。",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 0)
            };

            content.Controls.Add(lblExe);
            content.Controls.Add(exePanel);
            content.Controls.Add(lblCfg);
            content.Controls.Add(cfgPanel);
            content.Controls.Add(lblHint);
            content.Controls.Add(lblSnapshot);
            content.Controls.Add(_chkUseSnapshot);
            content.Controls.Add(_chkCloseBeforeSave);
            content.Controls.Add(lblSnapRoot);
            content.Controls.Add(snapRootPanel);
            content.Controls.Add(lblSnapHint);
            content.Controls.Add(_chkDisablePullout);
            content.Controls.Add(lblRestart);

            // ---------- 按钮区 ----------
            var btnPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 16, 0, 0),
                Padding = new Padding(0)
            };

            _btnSave = new Button
            {
                Text = "保存",
                Size = new Size(120, 32),
                Margin = new Padding(0, 0, 12, 0)
            };
            _btnSave.Click += OnSave;

            _lblStatus = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                ForeColor = Color.FromArgb(0, 128, 64),
                Text = ""
            };

            btnPanel.Controls.Add(_btnSave);
            btnPanel.Controls.Add(_lblStatus);

            root.Controls.Add(content, 0, 0);
            root.Controls.Add(btnPanel, 0, 1);

            Controls.Add(root);
        }

        // ------------------------------------------------------------
        //  读取当前配置到界面
        // ------------------------------------------------------------
        public void LoadFromSettings()
        {
            AppSettings.Reload();
            var s = AppSettings.Current;

            _txtExe.Text = s.BattleNetExePath ?? "";
            _txtConfig.Text = s.BattleNetConfigPath ?? "";
            _chkDisablePullout.Checked = s.DisablePullout;
            _chkUseSnapshot.Checked = s.UseSnapshotOnSwitch;
            _chkCloseBeforeSave.Checked = s.CloseBattleNetBeforeSave;
            _txtSnapshotRoot.Text = s.SnapshotRootPath ?? "";

            _lblStatus.Text = "";
        }

        // ------------------------------------------------------------
        //  保存
        // ------------------------------------------------------------
        private void OnSave(object? sender, EventArgs e)
        {
            string exe = _txtExe.Text.Trim();
            string cfg = _txtConfig.Text.Trim();
            string snapRoot = _txtSnapshotRoot.Text.Trim();

            if (!string.IsNullOrEmpty(exe) && !File.Exists(exe))
            {
                MessageBox.Show(this, "指定的战网程序路径不存在。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(cfg) && !File.Exists(cfg))
            {
                MessageBox.Show(this, "指定的配置文件路径不存在。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 快照根目录做基本校验：如果填了，看看能不能创建
            if (!string.IsNullOrEmpty(snapRoot))
            {
                try
                {
                    Directory.CreateDirectory(snapRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "快照根目录无效或无法创建：" + ex.Message,
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            bool oldDisable = AppSettings.Current.DisablePullout;

            AppSettings.Current.BattleNetExePath = string.IsNullOrEmpty(exe) ? null : exe;
            AppSettings.Current.BattleNetConfigPath = string.IsNullOrEmpty(cfg) ? null : cfg;
            AppSettings.Current.DisablePullout = _chkDisablePullout.Checked;
            AppSettings.Current.UseSnapshotOnSwitch = _chkUseSnapshot.Checked;
            AppSettings.Current.CloseBattleNetBeforeSave = _chkCloseBeforeSave.Checked;
            AppSettings.Current.SnapshotRootPath =
                string.IsNullOrEmpty(snapRoot) ? null : snapRoot;

            AppSettings.Save();
            AppSettings.Reload();

            _lblStatus.Text = "已保存 ✓";

            bool needRestart = oldDisable != _chkDisablePullout.Checked;
            Saved?.Invoke(this, needRestart);
        }
    }
}
