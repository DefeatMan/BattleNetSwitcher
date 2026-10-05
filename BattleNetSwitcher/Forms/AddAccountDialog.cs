#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BattleNetSwitcher.Core;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 添加账号到某个区服。
    ///
    /// 两种用法：
    ///   1. 邮箱填好 → 直接添加（要求该邮箱已在 Battle.net.config 的 SavedAccountNames 里）
    ///   2. 邮箱留空 → 点“启动战网并等待登录”，本窗口会以指定区服打开战网，
    ///      用户在客户端里登录新号，登录完成后自动检测到并回填邮箱，
    ///      点“确定”写入本地账号本。
    ///
    /// 说明：战网不对外提供“登录完成”事件，所以这里的检测是
    /// **轮询 Battle.net.config 的 SavedAccountNames，看是否出现了新的邮箱**
    /// （前提是用户在客户端里勾了“记住密码”）。
    ///
    /// 本窗口以非模态方式显示，否则用户去客户端登录时轮询逻辑无法与登录操作并存。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class AddAccountDialog : Form
    {
        private TextBox _txtEmail = null!;
        private ComboBox _cmbRegion = null!;
        private Button _btnLaunch = null!;
        private Button _btnRescan = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;
        private Label _lblStatus = null!;
        private Label _lblHint = null!;

        /// <summary>用户输入的邮箱（成功检测到登录后会回填）。</summary>
        public string Email { get; private set; } = "";

        /// <summary>用户选择的区服代码（CN / US / EU / KR / TW）。</summary>
        public string SelectedRegion { get; private set; } = "CN";

        /// <summary>用户是否点了确定（非模态窗口不能用 DialogResult 判断）。</summary>
        public bool Confirmed { get; private set; }

        // ------------------------------------------------------------
        //  登录检测状态
        // ------------------------------------------------------------
        /// <summary>点“启动并等待”之前的已记住账号集合，用于识别新增项。</summary>
        private HashSet<string> _baseline = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _waitCts;
        private DateTime _waitStartedAt;
        private const int WaitTimeoutSeconds = 180;   // 3 分钟
        private const int PollIntervalMs = 800;

        public AddAccountDialog(string presetRegion = "CN")
        {
            Text = "添加账号到区服";
            // CenterParent 只对 ShowDialog（模态）生效；
            // 本窗口是非模态的（Show(owner)），必须自己算位置，
            // 否则会退化到屏幕左上角。见 OnShown。
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(460, 268);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            PreselectRegion(presetRegion);
        }

        // ------------------------------------------------------------
        //  界面
        // ------------------------------------------------------------
        private void BuildUi()
        {
            var lblRegion = new Label
            {
                Text = "区服：",
                Location = new Point(20, 24),
                AutoSize = true
            };

            _cmbRegion = new ComboBox
            {
                Location = new Point(90, 21),
                Size = new Size(340, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbRegion.Items.AddRange(RegionInfo.All);
            _cmbRegion.SelectedIndex = 0;

            var lblEmail = new Label
            {
                Text = "邮箱：",
                Location = new Point(20, 62),
                AutoSize = true
            };

            _txtEmail = new TextBox
            {
                Location = new Point(90, 59),
                Size = new Size(340, 25)
            };

            _lblHint = new Label
            {
                Text = "邮箱留空 = 直接打开该区服的战网登录一个新号（登录时请勾选“记住密码”）。",
                Location = new Point(20, 92),
                Size = new Size(420, 36),
                ForeColor = Color.DimGray
            };

            _btnLaunch = new Button
            {
                Text = "启动战网并等待登录",
                Location = new Point(20, 134),
                Size = new Size(180, 30)
            };
            _btnLaunch.Click += OnLaunchAndWait;

            _btnRescan = new Button
            {
                Text = "刷新检测",
                Location = new Point(208, 134),
                Size = new Size(100, 30)
            };
            _btnRescan.Click += (_, _) => ScanForNewAccounts(manual: true);

            _lblStatus = new Label
            {
                Text = "",
                Location = new Point(20, 172),
                Size = new Size(420, 36),
                ForeColor = Color.DimGray
            };

            _btnOk = new Button
            {
                Text = "确定",
                Location = new Point(228, 218),
                Size = new Size(100, 30),
                DialogResult = DialogResult.None
            };
            _btnOk.Click += OnOk;

            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(336, 218),
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[]
            {
                lblRegion, _cmbRegion, lblEmail, _txtEmail, _lblHint,
                _btnLaunch, _btnRescan, _lblStatus, _btnOk, _btnCancel
            });

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void PreselectRegion(string code)
        {
            for (int i = 0; i < _cmbRegion.Items.Count; i++)
            {
                if (_cmbRegion.Items[i] is RegionInfo ri &&
                    string.Equals(ri.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbRegion.SelectedIndex = i;
                    return;
                }
            }
        }

        private RegionInfo? CurrentRegion => _cmbRegion.SelectedItem as RegionInfo;

        // ------------------------------------------------------------
        //  启动战网并等待登录
        // ------------------------------------------------------------
        private async void OnLaunchAndWait(object? sender, EventArgs e)
        {
            var region = CurrentRegion;
            if (region == null)
            {
                MessageBox.Show(this, "请选择区服。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 记录基线：启动前已经"记住密码"的账号
            try
            {
                _baseline = new HashSet<string>(AccountSwitcher.LoadAccounts(),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            SetWaiting(true, $"正在以 [{region.DisplayName}] 启动战网…");

            try
            {
                await Task.Run(() => AccountSwitcher.LaunchForRegion(region.Region));
                SetWaiting(false, "");
                _lblStatus.ForeColor = Color.FromArgb(0, 100, 160);
                _lblStatus.Text = $"已启动 [{region.DisplayName}] 的战网客户端。\r\n" +
                                  "请在客户端里登录新账号（勾选“记住密码”），登录完成后会自动填到上面的邮箱框。";
                StartPolling();
            }
            catch (Exception ex)
            {
                SetWaiting(false, "启动失败：" + ex.Message);
                _lblStatus.ForeColor = Color.FromArgb(180, 40, 40);
                MessageBox.Show(this, ex.Message, "启动失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>后台轮询 Battle.net.config，发现新的已记住账号就回填。</summary>
        private void StartPolling()
        {
            StopPolling();
            _waitCts = new CancellationTokenSource();
            _waitStartedAt = DateTime.Now;
            var token = _waitCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var saved = AccountSwitcher.LoadAccounts();
                        var added = saved
                            .Where(x => !_baseline.Contains(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (added.Count > 0)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (IsDisposed) return;

                                _txtEmail.Text = added[0];
                                _lblStatus.ForeColor = Color.FromArgb(0, 128, 64);
                                _lblStatus.Text = added.Count == 1
                                    ? $"✅ 检测到新登录的账号：{added[0]}\r\n点“确定”把它加入本地账号本。"
                                    : $"✅ 检测到 {added.Count} 个新账号，已填入第一个：{added[0]}\r\n" +
                                      "  其余：" + string.Join("、", added.Skip(1));
                                StopPollingAndResetButton();
                            }));
                            return;
                        }

                        int waited = (int)(DateTime.Now - _waitStartedAt).TotalSeconds;
                        if (waited >= WaitTimeoutSeconds)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (IsDisposed) return;
                                SetWaiting(false, "");
                                _lblStatus.ForeColor = Color.FromArgb(160, 100, 0);
                                _lblStatus.Text = $"等待超时（{WaitTimeoutSeconds} 秒）仍未检测到新账号。\r\n" +
                                                  "请确认登录时勾选了“记住密码”；登录完成后也可点“刷新检测”。";
                            }));
                            return;
                        }

                        if (waited % 5 == 0)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (IsDisposed) return;
                                _lblStatus.Text = $"等待登录完成… 已等待 {waited} 秒\r\n" +
                                                  "（登录时请勾选“记住密码”，否则无法被检测到）";
                            }));
                        }
                    }
                    catch
                    {
                        // 配置文件可能正被客户端写入，读取失败是正常的，下一轮再试
                    }

                    try { await Task.Delay(PollIntervalMs, token); }
                    catch (TaskCanceledException) { return; }
                }
            }, token);
        }

        /// <summary>手动刷新一次：读当前已记住的账号，把新出现的填进去。</summary>
        private void ScanForNewAccounts(bool manual)
        {
            List<string> saved;
            try
            {
                saved = AccountSwitcher.LoadAccounts();
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = Color.FromArgb(180, 40, 40);
                _lblStatus.Text = "读取战网配置失败：" + ex.Message;
                return;
            }

            var added = saved
                .Where(x => !_baseline.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (added.Count > 0)
            {
                _txtEmail.Text = added[0];
                _lblStatus.ForeColor = Color.FromArgb(0, 128, 64);
                _lblStatus.Text = $"✅ 检测到新登录的账号：{added[0]}";
                return;
            }

            if (manual)
            {
                _lblStatus.ForeColor = Color.FromArgb(160, 100, 0);
                _lblStatus.Text = saved.Count == 0
                    ? "战网里还没有记住任何账号。请在客户端登录并勾选“记住密码”。"
                    : $"没有检测到新增账号（战网已记住 {saved.Count} 个）。\r\n" +
                      "如果你登录的是已经记住过的账号，直接用上面的“确定”即可。";
            }
        }

        private void SetWaiting(bool waiting, string status)
        {
            _btnLaunch.Enabled = !waiting;
            _btnLaunch.Text = waiting ? "等待登录中…" : "启动战网并等待登录";
            _lblStatus.Text = status;
        }

        /// <summary>只停止轮询，不改动界面文字。</summary>
        private void StopPolling()
        {
            try { _waitCts?.Cancel(); } catch { }
            _waitCts?.Dispose();
            _waitCts = null;
        }

        /// <summary>停止轮询并把“等待登录”按钮恢复成可点。</summary>
        private void StopPollingAndResetButton()
        {
            StopPolling();
            SetWaiting(false, _lblStatus.Text);
        }

        // ------------------------------------------------------------
        //  确定
        // ------------------------------------------------------------
        private void OnOk(object? sender, EventArgs e)
        {
            var region = CurrentRegion;
            if (region == null)
            {
                MessageBox.Show(this, "请选择区服。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string email = _txtEmail.Text.Trim();

            // 邮箱留空时，先尝试自动补一次（用户可能刚登录完但没等到轮询）
            if (string.IsNullOrEmpty(email))
            {
                ScanForNewAccounts(manual: false);
                email = _txtEmail.Text.Trim();
            }

            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(this,
                    "请输入邮箱，或点“启动战网并等待登录”用客户端登录一个新号。\r\n\r\n" +
                    "注意：登录时请勾选“记住密码”，否则工具无法读取到该账号。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 校验邮箱必须已在 SavedAccountNames 里
            try
            {
                var saved = AccountSwitcher.LoadAccounts();
                bool exists = saved.Any(s => string.Equals(s, email, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    MessageBox.Show(this,
                        $"邮箱 {email} 未在战网中保存。\r\n\r\n" +
                        "请先在战网客户端里用该账号登录一次，" +
                        "并勾选“记住密码”，然后再回到这里添加。",
                        "账号未保存",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "读取战网配置失败：" + ex.Message,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Email = email;
            SelectedRegion = region.Code;
            Confirmed = true;

            StopPolling();
            Close();
        }

        /// <summary>
        /// 非模态窗口按父窗口（主窗口）居中显示，而不是飞到屏幕左上角。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterOnOwner();
        }

        private void CenterOnOwner()
        {
            Rectangle area;

            if (Owner != null && Owner.Visible)
            {
                area = Owner.Bounds;
            }
            else
            {
                // 没有父窗口时兜底：屏幕工作区居中
                area = Screen.FromPoint(Cursor.Position).WorkingArea;
            }

            int x = area.Left + (area.Width - Width) / 2;
            int y = area.Top + (area.Height - Height) / 2;

            // 别超出工作区
            var wa = Screen.FromRectangle(area).WorkingArea;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));

            Location = new Point(x, y);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPolling();
            base.OnFormClosed(e);
        }
    }
}
