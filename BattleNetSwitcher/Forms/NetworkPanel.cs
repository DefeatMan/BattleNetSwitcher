#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using BattleNetSwitcher.Core;
using Microsoft.Win32;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 一键拔线页：选择目标应用 → 断网 N 秒（可选同时静音）→ 自动恢复。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class NetworkPanel : UserControl
    {
        private TextBox _txtAppPath = null!;
        private Button _btnBrowse = null!;
        private Button _btnPick = null!;
        private CheckBox _chkMute = null!;
        private NumericUpDown _numSeconds = null!;
        private Button _btnPull = null!;
        private Label _lblCountdown = null!;

        private string? _appPath;
        private string? _procName;
        private string? _ruleName;
        private bool _busy;

        private const string RegKey = @"Software\BattleNetTool";

        public NetworkPanel()
        {
            BuildUi();
            LoadSettings();
            UpdateButtonStates();
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
                RowCount = 3,
                Padding = new Padding(12),
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));     // 目标应用行
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));    // 一键拔线分组
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));     // 剩余

            // ---------- 目标应用行 ----------
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var lblApp = new Label
            {
                Text = "目标应用：",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            _txtAppPath = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSize = false,
                Margin = new Padding(0, 0, 6, 0)
            };

            _btnBrowse = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnBrowse.Click += OnBrowse;

            _btnPick = new Button
            {
                Text = "选择…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            _btnPick.Click += OnPickFromProcess;

            topPanel.Controls.Add(lblApp, 0, 0);
            topPanel.Controls.Add(_txtAppPath, 1, 0);
            topPanel.Controls.Add(_btnBrowse, 2, 0);
            topPanel.Controls.Add(_btnPick, 3, 0);

            // ---------- 一键拔线分组（手工定位，杜绝 TableLayoutPanel 居中差异）----------
            var grp = new GroupBox
            {
                Text = "一键拔线",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0)
            };

            // Row 0：静音 CheckBox
            _chkMute = new CheckBox
            {
                Location = new Point(15, 25),
                AutoSize = true,
                Text = "同时静音该应用的声音",
                Checked = true
            };

            // Row 1：三控件，[Label][NUD][Button] 用相同的中线
            //
            //   Label  高 ~15 → Y = 67（中心 74.5）
            //   NUD    高  25 → Y = 62（中心 74.5）
            //   Button 高  25 → Y = 62（中心 74.5）
            //
            //   NUD 和 Button 的 Y 与 Height 完全相同 → 必然对齐
            var lblSec = new Label
            {
                Location = new Point(15, 67),
                AutoSize = true,
                Text = "断网时长（秒）："
            };

            _numSeconds = new NumericUpDown
            {
                Location = new Point(130, 62),
                Size = new Size(80, 25),
                Minimum = 1,
                Maximum = 3600,
                Value = 3
            };
            _numSeconds.ValueChanged += (_, _) => SaveSettings();

            _btnPull = new Button
            {
                Location = new Point(230, 62),      // ★ 和 NUD 完全相同的 Y
                Size = new Size(130, 25),           // ★ 和 NUD 完全相同的高度
                AutoSize = false,                   // ★ 显式关闭，防止被忽略
                Text = "一键拔线"
            };
            _btnPull.Click += OnPullCable;

            // Row 2：倒计时
            _lblCountdown = new Label
            {
                Location = new Point(15, 100),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = ""
            };

            grp.Controls.AddRange(new Control[]
            {
                _chkMute, lblSec, _numSeconds, _btnPull, _lblCountdown
            });

            // ---------- 提示 ----------
            var lblHint = new Label
            {
                Text = "提示：修改防火墙规则需要管理员权限（清单已自动请求）。",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 8, 0, 0)
            };

            root.Controls.Add(topPanel, 0, 0);
            root.Controls.Add(grp, 0, 1);
            root.Controls.Add(lblHint, 0, 2);

            Controls.Add(root);
        }

        private void UpdateButtonStates()
        {
            bool hasApp = !string.IsNullOrEmpty(_appPath) && !string.IsNullOrEmpty(_ruleName);

            _btnBrowse.Enabled = !_busy;
            _btnPick.Enabled = !_busy;
            _btnPull.Enabled = hasApp && !_busy;
            _numSeconds.Enabled = !_busy;
            _chkMute.Enabled = !_busy;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateButtonStates();
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        // ------------------------------------------------------------
        //  选择目标应用
        // ------------------------------------------------------------
        private void OnBrowse(object? sender, EventArgs e)
        {
            if (_busy) return;

            using var dlg = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择要绑定的应用"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SetTargetApp(dlg.FileName);
        }

        private void OnPickFromProcess(object? sender, EventArgs e)
        {
            if (_busy) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                using var dlg = new ProcessPickerDialog();
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrEmpty(dlg.SelectedPath)) return;
                SetTargetApp(dlg.SelectedPath);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void SetTargetApp(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            if (!string.IsNullOrEmpty(_ruleName))
            {
                try { FirewallManager.DeleteRule(_ruleName); } catch { }
                if (!string.IsNullOrEmpty(_procName))
                    AudioSessionController.MuteByNameAsync(_procName, false);
            }

            _appPath = path;
            _procName = Path.GetFileNameWithoutExtension(path);
            _ruleName = "BattleNetTool_" + _procName + "_" +
                        Guid.NewGuid().ToString("N").Substring(0, 8);

            _txtAppPath.Text = _appPath;
            _lblCountdown.Text = "";

            SaveSettings();
            UpdateButtonStates();
        }

        private bool EnsureAppSelected()
        {
            if (!string.IsNullOrEmpty(_appPath)) return true;

            MessageBox.Show(this, "请先点击“浏览…”或“选择…”指定目标应用。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        // ------------------------------------------------------------
        //  一键拔线
        // ------------------------------------------------------------
        private async void OnPullCable(object? sender, EventArgs e)
        {
            if (_busy || !EnsureAppSelected()) return;

            SetBusy(true);

            string ruleName = _ruleName!;
            string procName = _procName!;
            bool mute = _chkMute.Checked;
            int seconds = (int)_numSeconds.Value;

            bool ruleCreated = false;

            try
            {
                await Task.Run(() => FirewallManager.CreateBlockRule(ruleName, _appPath!));
                ruleCreated = true;

                if (mute) AudioSessionController.MuteByNameAsync(procName, true);

                for (int i = seconds; i > 0; i--)
                {
                    _lblCountdown.Text = $"已断网，{i} 秒后自动恢复…";
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "拔线失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (ruleCreated)
                {
                    try
                    {
                        await Task.Run(() => FirewallManager.SetRuleEnabled(ruleName, false));
                        await Task.Run(() => FirewallManager.DeleteRule(ruleName));
                    }
                    catch (Exception ex)
                    {
                        _lblCountdown.Text = "恢复失败：" + ex.Message;
                    }
                }

                if (mute) AudioSessionController.MuteByNameAsync(procName, false);

                if (_lblCountdown.Text.StartsWith("已断网", StringComparison.Ordinal))
                    _lblCountdown.Text = "已恢复。";

                SetBusy(false);
            }
        }

        // ------------------------------------------------------------
        //  退出时清理
        // ------------------------------------------------------------
        public void CleanupRules()
        {
            if (string.IsNullOrEmpty(_ruleName)) return;

            try
            {
                if (!string.IsNullOrEmpty(_procName))
                    AudioSessionController.MuteByNameAsync(_procName, false);
                FirewallManager.DeleteRule(_ruleName);
            }
            catch { /* 忽略 */ }
        }

        // ------------------------------------------------------------
        //  配置持久化
        // ------------------------------------------------------------
        private void LoadSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKey);
                if (key == null) return;

                _appPath = key.GetValue("AppPath") as string;
                _procName = key.GetValue("ProcName") as string;
                _ruleName = key.GetValue("RuleName") as string;

                if (int.TryParse(key.GetValue("PullSeconds")?.ToString(), out int sec) &&
                    sec >= (int)_numSeconds.Minimum && sec <= (int)_numSeconds.Maximum)
                {
                    _numSeconds.Value = sec;
                }

                if (!string.IsNullOrEmpty(_appPath))
                {
                    if (string.IsNullOrEmpty(_procName))
                        _procName = Path.GetFileNameWithoutExtension(_appPath);

                    if (string.IsNullOrEmpty(_ruleName))
                        _ruleName = "BattleNetTool_" + _procName + "_" +
                                    Guid.NewGuid().ToString("N").Substring(0, 8);

                    _txtAppPath.Text = _appPath;
                }
            }
            catch { /* 忽略 */ }
        }

        private void SaveSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegKey);
                if (key == null) return;

                key.SetValue("AppPath", _appPath ?? "");
                key.SetValue("ProcName", _procName ?? "");
                key.SetValue("RuleName", _ruleName ?? "");
                key.SetValue("PullSeconds", (int)_numSeconds.Value);
            }
            catch { /* 忽略 */ }
        }
    }
}
