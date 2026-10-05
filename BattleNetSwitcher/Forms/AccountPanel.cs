#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using BattleNetSwitcher.Core;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 账号切换页：
    ///   * 顶部区服下拉框（只列出有账号的区服）
    ///   * 中部账号列表（当前区服的账号）
    ///   * 底部添加 / 刷新 / 自动重启
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class AccountPanel : UserControl
    {
        private ComboBox _cmbRegion = null!;
        private Label _lblEmpty = null!;
        private FlowLayoutPanel _listPanel = null!;
        private Button _btnAdd = null!;
        private Button _btnRefresh = null!;
        private CheckBox _chkRestart = null!;
        private Label _lblStatus = null!;

        private bool _busy;

        /// <summary>
        /// 允许在 _busy 期间强行刷新列表。
        /// 用于切换/添加/移除流程的收尾刷新：那时 _busy 仍为 true，
        /// 而列表必须立刻更新到最新状态，否则界面会停在旧数据上。
        /// </summary>
        private bool _forceRefresh;

        public AccountPanel()
        {
            BuildUi();

            // 首次初始化本地账号本
            try { AccountBook.EnsureInitialized(); } catch { }

            ReloadRegions();
            RefreshAccountList();
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
                RowCount = 4,
                Padding = new Padding(12),
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 区服选择行
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 列表
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 状态
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 按钮行

            // ---- 区服选择行 ----
            var regionPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0)
            };

            var lblRegion = new Label
            {
                Text = "区服：",
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            };

            _cmbRegion = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200,
                Margin = new Padding(0, 2, 12, 0)
            };
            _cmbRegion.SelectedIndexChanged += (_, _) =>
            {
                RefreshAccountList();
                SaveLastRegion();
            };

            var lblHint = new Label
            {
                Text = "（切换时通过 --setregion 启动参数生效）",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 6, 0, 0)
            };

            regionPanel.Controls.Add(lblRegion);
            regionPanel.Controls.Add(_cmbRegion);
            regionPanel.Controls.Add(lblHint);

            // ---- 账号列表 ----
            _listPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _listPanel.ClientSizeChanged += (_, _) => ResizeRows();
            _listPanel.ControlAdded += (_, e) =>
            {
                if (e.Control != null) ResizeRow(e.Control);
            };

            // 空列表时显示的提示
            _lblEmpty = new Label
            {
                Text = "本区服暂无账号。点击下方“添加账号”加入。",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Visible = false
            };
            _lblEmpty.SizeChanged += (_, _) => ResizeRow(_lblEmpty);

            // ---- 状态 ----
            _lblStatus = new Label
            {
                AutoSize = true,
                Text = "",
                Margin = new Padding(0, 8, 0, 8)
            };

            // ---- 按钮行 ----
            var btnPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _btnAdd = new Button
            {
                Text = "添加账号",
                Size = new Size(120, 32),
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnAdd.Click += OnAddAccount;

            _btnRefresh = new Button
            {
                Text = "刷新列表",
                Size = new Size(120, 32),
                Margin = new Padding(0, 0, 12, 0)
            };
            _btnRefresh.Click += (_, _) =>
            {
                ReloadRegions();
                RefreshAccountList();
            };

            _chkRestart = new CheckBox
            {
                Text = "切换后自动重启战网",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            btnPanel.Controls.Add(_btnAdd);
            btnPanel.Controls.Add(_btnRefresh);
            btnPanel.Controls.Add(_chkRestart);

            root.Controls.Add(regionPanel, 0, 0);
            root.Controls.Add(_listPanel, 0, 1);
            root.Controls.Add(_lblStatus, 0, 2);
            root.Controls.Add(btnPanel, 0, 3);

            Controls.Add(root);
        }

        // ------------------------------------------------------------
        //  区服下拉框
        // ------------------------------------------------------------
        private void ReloadRegions()
        {
            string? selected = GetSelectedRegionCode();

            var codes = AccountBook.GetActiveRegions();

            _cmbRegion.BeginUpdate();
            try
            {
                _cmbRegion.Items.Clear();
                foreach (var code in codes)
                {
                    var info = RegionInfo.TryFromCode(code);
                    if (info != null) _cmbRegion.Items.Add(info);
                }
            }
            finally
            {
                _cmbRegion.EndUpdate();
            }

            // 恢复原选中
            if (codes.Count > 0)
            {
                int index = 0;
                if (!string.IsNullOrEmpty(selected))
                {
                    for (int i = 0; i < _cmbRegion.Items.Count; i++)
                    {
                        if (_cmbRegion.Items[i] is RegionInfo ri &&
                            string.Equals(ri.Code, selected, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                _cmbRegion.SelectedIndex = index;
            }
        }

        private string? GetSelectedRegionCode()
        {
            return (_cmbRegion.SelectedItem as RegionInfo)?.Code;
        }

        private RegionInfo? GetSelectedRegionInfo()
        {
            return _cmbRegion.SelectedItem as RegionInfo;
        }

        private void SaveLastRegion()
        {
            try
            {
                string? code = GetSelectedRegionCode();
                if (string.IsNullOrEmpty(code)) return;

                using var key = Microsoft.Win32.Registry.CurrentUser
                    .CreateSubKey(@"Software\BattleNetTool");
                key?.SetValue("LastUiRegion", code);
            }
            catch { }
        }

        private string LoadLastRegion()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\BattleNetTool");
                return key?.GetValue("LastUiRegion") as string ?? "CN";
            }
            catch
            {
                return "CN";
            }
        }

        // ------------------------------------------------------------
        //  账号列表
        // ------------------------------------------------------------
        private void RefreshAccountList()
        {
            // 正常忙碌期间不刷新（避免和切换流程抢控件）；
            // 但切换/添加结束时的收尾刷新必须能穿透 —— 那时 _busy 还是 true
            // （SetBusy(false) 在 finally 里还没执行），一刀切地 return 会让列表
            // 停留在旧数据上，表现就是"点了切换没反应，·当前 还在原来的账号"。
            if (_busy && !_forceRefresh) return;

            var region = GetSelectedRegionInfo();
            if (region == null)
            {
                _listPanel.Controls.Clear();
                _lblEmpty.Visible = true;
                _lblEmpty.Text = "还没有任何账号。点击下方“添加账号”加入第一个。";
                _listPanel.Controls.Add(_lblEmpty);
                ResizeRow(_lblEmpty);
                _lblStatus.Text = "";
                return;
            }

            var emails = AccountBook.GetEmailsForRegion(region.Code);

            // 判断哪个是当前自动登录账号（SavedAccountNames 首位）
            string? currentFirst = null;
            string? currentRegionCode = null;
            try
            {
                var (saved, selectedRegion) = AccountSwitcher.ReadConfigAccountsAndRegion();
                if (saved.Count > 0) currentFirst = saved[0];
                currentRegionCode = selectedRegion;
            }
            catch { }

            _listPanel.SuspendLayout();
            try
            {
                for (int i = _listPanel.Controls.Count - 1; i >= 0; i--)
                {
                    var c = _listPanel.Controls[i];
                    _listPanel.Controls.RemoveAt(i);
                    c.Dispose();
                }

                if (emails.Count == 0)
                {
                    _lblEmpty.Visible = true;
                    _lblEmpty.Text = $"区服 [{region.DisplayName}] 暂无账号。";
                    _listPanel.Controls.Add(_lblEmpty);
                    ResizeRow(_lblEmpty);
                }
                else
                {
                    _lblEmpty.Visible = false;

                    foreach (var email in emails)
                    {
                        bool isCurrent = !string.IsNullOrEmpty(currentFirst) &&
                                         string.Equals(email, currentFirst,
                                                       StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(region.Code, currentRegionCode,
                                                       StringComparison.OrdinalIgnoreCase);

                        var row = new AccountRow(email, isCurrent);
                        row.SwitchRequested += OnRowSwitchRequested;
                        row.RemoveRequested += OnRowRemoveRequested;
                        _listPanel.Controls.Add(row);
                    }

                    ResizeRows();
                }
            }
            finally
            {
                _listPanel.ResumeLayout();
            }

            _lblStatus.Text = $"区服 [{region.DisplayName}] 下共 {emails.Count} 个账号。";
        }

        private void ResizeRows()
        {
            foreach (Control c in _listPanel.Controls)
                ResizeRow(c);
        }

        private void ResizeRow(Control c)
        {
            int width = _listPanel.ClientSize.Width - 4;
            if (width < 200) width = 200;
            c.Width = width;
        }

        // ------------------------------------------------------------
        //  添加
        // ------------------------------------------------------------
        private void OnAddAccount(object? sender, EventArgs e)
        {
            if (_busy) return;

            string preset = GetSelectedRegionCode() ?? LoadLastRegion();

            // 非模态：用户可能在窗口开着的时候去战网客户端登录新号，
            // 窗口里的“等待登录完成”轮询需要和登录操作并存。
            var dlg = new AddAccountDialog(preset);
            dlg.FormClosed += (_, _) =>
            {
                if (!dlg.Confirmed) return;

                if (AccountBook.Add(dlg.Email, dlg.SelectedRegion))
                {
                    ReloadRegions();
                    // 切到刚添加的区服
                    for (int i = 0; i < _cmbRegion.Items.Count; i++)
                    {
                        if (_cmbRegion.Items[i] is RegionInfo ri &&
                            string.Equals(ri.Code, dlg.SelectedRegion,
                                          StringComparison.OrdinalIgnoreCase))
                        {
                            _cmbRegion.SelectedIndex = i;
                            break;
                        }
                    }
                    RefreshAccountList();
                }
                else
                {
                    MessageBox.Show(this,
                        $"{dlg.Email} 已经在 {dlg.SelectedRegion} 区服中。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            dlg.Show(this);   // 非模态
        }

        // ------------------------------------------------------------
        //  切换
        // ------------------------------------------------------------
        private async void OnRowSwitchRequested(object? sender, string email)
        {
            if (_busy || string.IsNullOrEmpty(email)) return;

            var region = GetSelectedRegionInfo();
            if (region == null) return;

            string prompt = $"确定切换到 {email} 吗？\r\n" +
                            $"区服：{region.DisplayName}\r\n" +
                            "将关闭战网、修改配置并重新启动。";

            var r = MessageBox.Show(this, prompt, "确认切换",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                bool restart = _chkRestart.Checked;

                string log = await Task.Run(() =>
                    AccountSwitcher.SwitchAccount(email, region.Region, restart));

                await RefreshWithRetryAsync(email, region.Code);

                MessageBox.Show(this,
                    string.IsNullOrWhiteSpace(log) ? "切换完成。" : log,
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "切换失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                // 同样要穿透：此刻 _busy 仍为 true
                _forceRefresh = true;
                try { RefreshAccountList(); }
                finally { _forceRefresh = false; }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RefreshWithRetryAsync(string expectFirst, string expectRegion)
        {
            const int attempts = 5;
            const int delayMs = 400;

            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    var (saved, region) = AccountSwitcher.ReadConfigAccountsAndRegion();
                    if (saved.Count > 0 &&
                        string.Equals(saved[0], expectFirst, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(region, expectRegion, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
                catch { }

                await Task.Delay(delayMs);
            }

            ReloadRegions();

            // 此刻 _busy 仍为 true（SetBusy(false) 在调用方的 finally 里），
            // 用 _forceRefresh 穿透守卫，保证列表立刻更新
            _forceRefresh = true;
            try { RefreshAccountList(); }
            finally { _forceRefresh = false; }
        }

        // ------------------------------------------------------------
        //  移除
        // ------------------------------------------------------------
        private void OnRowRemoveRequested(object? sender, string email)
        {
            if (_busy) return;

            var region = GetSelectedRegionInfo();
            if (region == null) return;

            try
            {
                if (AccountBook.Remove(email, region.Code))
                {
                    ReloadRegions();
                    RefreshAccountList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "移除失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------
        //  忙碌状态
        // ------------------------------------------------------------
        private void SetBusy(bool busy)
        {
            _busy = busy;
            _btnAdd.Enabled = !busy;
            _btnRefresh.Enabled = !busy;
            _chkRestart.Enabled = !busy;
            _cmbRegion.Enabled = !busy;
            _listPanel.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
    }
}
