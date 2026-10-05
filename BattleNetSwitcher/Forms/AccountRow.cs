#nullable enable
using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 账号列表中的一行：[邮箱] [● 当前] [切换按钮] [× 移除]
    /// 双击整行也可触发切换。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class AccountRow : UserControl
    {
        private readonly string _email;
        private readonly Label _lblEmail;
        private readonly Label _lblCurrent;
        private readonly Button _btnSwitch;
        private readonly Button _btnRemove;

        public event EventHandler<string>? SwitchRequested;
        public event EventHandler<string>? RemoveRequested;

        public string Email => _email;

        public AccountRow(string email, bool isCurrent)
        {
            _email = email;

            Height = 48;                          // ★ 从 44 加到 48，给按钮留够高度
            Width = 400;
            MinimumSize = new Size(240, 48);
            Margin = new Padding(0);
            Padding = new Padding(0);
            BackColor = isCurrent ? Color.FromArgb(232, 244, 255) : Color.White;
            Cursor = Cursors.Hand;

            // [邮箱 100%] [● 当前 64px] [切换 84px] [× 36px]
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(12, 6, 8, 6)     // ★ 上下对称 6，左右不对称没问题
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _lblEmail = new Label
            {
                Text = email,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            _lblCurrent = new Label
            {
                Text = "● 当前",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 128, 64),
                Margin = new Padding(0),
                Visible = isCurrent,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            // ★ 切换按钮：不加自定义 Font，不加 TextAlign，margin 完全对称
            _btnSwitch = new Button
            {
                Text = "切换",
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 4, 4),
                UseVisualStyleBackColor = true
            };
            _btnSwitch.Click += (_, _) => SwitchRequested?.Invoke(this, _email);

            // ★ × 按钮：同样不加 Font 不加 TextAlign，只用 inherit 字体
            _btnRemove = new Button
            {
                Text = "×",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 4, 0, 4),
                UseVisualStyleBackColor = true
                // 不设置 Font，继承窗体默认，与“切换”按钮一致
            };
            _btnRemove.Click += (_, _) =>
            {
                var r = MessageBox.Show(
                    $"确定要从本区服移除账号 {email} 吗？\r\n" +
                    "（仅移除本地记录，不会影响战网账号本身）",
                    "确认移除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (r == DialogResult.Yes)
                    RemoveRequested?.Invoke(this, _email);
            };

            table.Controls.Add(_lblEmail, 0, 0);
            table.Controls.Add(_lblCurrent, 1, 0);
            table.Controls.Add(_btnSwitch, 2, 0);
            table.Controls.Add(_btnRemove, 3, 0);

            Controls.Add(table);

            // 双击整行触发切换
            DoubleClick += (_, _) => SwitchRequested?.Invoke(this, _email);
            table.DoubleClick += (_, _) => SwitchRequested?.Invoke(this, _email);
            _lblEmail.DoubleClick += (_, _) => SwitchRequested?.Invoke(this, _email);
            _lblCurrent.DoubleClick += (_, _) => SwitchRequested?.Invoke(this, _email);

            if (!isCurrent)
            {
                void enter(object? s, EventArgs e) => BackColor = Color.FromArgb(245, 248, 252);
                void leave(object? s, EventArgs e) => BackColor = Color.White;

                MouseEnter += enter; MouseLeave += leave;
                table.MouseEnter += enter; table.MouseLeave += leave;
                _lblEmail.MouseEnter += enter; _lblEmail.MouseLeave += leave;
                _lblCurrent.MouseEnter += enter; _lblCurrent.MouseLeave += leave;
            }
        }
    }
}
