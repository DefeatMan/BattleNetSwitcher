#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace BattleNetSwitcher.Forms
{
    /// <summary>
    /// 列出当前运行的进程（含完整路径），支持按进程名或路径搜索。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ProcessPickerDialog : Form
    {
        // 缓存枚举到的进程（一次枚举，多次过滤）
        private sealed class ProcessInfo
        {
            public string Name = "";
            public int Pid;
            public string Path = "";
        }

        private ListView _listView = null!;
        private TextBox _txtSearch = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;
        private Button _btnRefresh = null!;
        private Label _lblCount = null!;

        private readonly List<ProcessInfo> _allProcesses = new();

        public string? SelectedPath { get; private set; }

        public ProcessPickerDialog()
        {
            Text = "从运行中的进程选择";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(800, 520);
            MinimumSize = new Size(560, 360);
            MinimizeBox = false;
            MaximizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            LoadProcesses();
        }

        private void BuildUi()
        {
            var lblSearch = new Label
            {
                Text = "搜索：",
                Location = new Point(12, 15),
                AutoSize = true
            };

            _txtSearch = new TextBox
            {
                Location = new Point(60, 12),
                Size = new Size(400, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _txtSearch.TextChanged += (_, _) => ApplyFilter();
            // 让回车直接“确定”
            _txtSearch.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    OnOk();
                }
            };

            _lblCount = new Label
            {
                Location = new Point(470, 15),
                Size = new Size(320, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = Color.DimGray
            };

            var lblTip = new Label
            {
                Text = "按进程名或路径匹配，不区分大小写；双击直接选中。",
                Location = new Point(12, 42),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _listView = new ListView
            {
                Location = new Point(12, 68),
                Size = new Size(776, 400),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = true
            };
            _listView.Columns.Add("进程名", 200);
            _listView.Columns.Add("PID", 80, HorizontalAlignment.Right);
            _listView.Columns.Add("可执行文件路径", 460);
            _listView.DoubleClick += (_, _) => OnOk();

            _btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(12, 478),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            _btnRefresh.Click += (_, _) => LoadProcesses();

            _btnOk = new Button
            {
                Text = "确定",
                Location = new Point(576, 478),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            _btnOk.Click += (_, _) => OnOk();

            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(684, 478),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[]
            {
                lblSearch, _txtSearch, _lblCount, lblTip,
                _listView, _btnRefresh, _btnOk, _btnCancel
            });

            CancelButton = _btnCancel;
            AcceptButton = _btnOk;
        }

        // ------------------------------------------------------------
        //  枚举进程
        // ------------------------------------------------------------
        private void LoadProcesses()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _allProcesses.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var proc in Process.GetProcesses())
                {
                    string? path;
                    try { path = proc.MainModule?.FileName; }
                    catch { continue; }   // 系统进程 / 权限不足

                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                    // 同一路径去重（多开进程）
                    if (!seen.Add(path)) continue;

                    _allProcesses.Add(new ProcessInfo
                    {
                        Name = proc.ProcessName,
                        Pid = proc.Id,
                        Path = path
                    });
                }

                _allProcesses.Sort((a, b) =>
                    string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                ApplyFilter();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ------------------------------------------------------------
        //  按当前搜索框过滤
        // ------------------------------------------------------------
        private void ApplyFilter()
        {
            string keyword = _txtSearch.Text.Trim();

            IEnumerable<ProcessInfo> source = _allProcesses;

            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(p =>
                    p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    p.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();

                foreach (var p in source)
                {
                    var item = new ListViewItem(p.Name);
                    item.SubItems.Add(p.Pid.ToString());
                    item.SubItems.Add(p.Path);
                    item.Tag = p.Path;
                    _listView.Items.Add(item);
                }

                // 恢复可能的选中（保持第一个选中，避免空白）
                if (_listView.Items.Count > 0 && _listView.SelectedItems.Count == 0)
                    _listView.Items[0].Selected = true;
            }
            finally
            {
                _listView.EndUpdate();
            }

            _lblCount.Text = string.IsNullOrEmpty(keyword)
                ? $"共 {_allProcesses.Count} 个进程"
                : $"{_listView.Items.Count} / {_allProcesses.Count} 个匹配";
        }

        // ------------------------------------------------------------
        //  确定
        // ------------------------------------------------------------
        private void OnOk()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先选择一个进程。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? path = _listView.SelectedItems[0].Tag as string;
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "无法获取该进程的可执行文件路径。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedPath = path;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
