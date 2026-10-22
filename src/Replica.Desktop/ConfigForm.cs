using System.Diagnostics;

namespace Replica.Desktop;

/// <summary>
/// 原软件 TConfigINI 的复刻：1501×998、bsSingle、poScreenCenter。
/// 左侧 sgConfig（3 列）显示并编辑 config.ini，右侧 sgHQAddress（4 列）
/// 列出各账号的行情/交易前置。底部增加搜索、重载、打开目录与关闭。
/// </summary>
internal sealed class ConfigForm : Form
{
    private readonly string _path;
    private readonly LogHub _log;
    private readonly DataGridView _grid = new();
    private readonly DataGridView _addresses = new();
    private readonly TextBox _search = new();
    private readonly Label _status = new();
    private readonly Label _pathLabel = new();

    private DesktopConfig _config;

    internal ConfigForm(ReplicaContext context, LogHub log)
    {
        _path = context.ConfigPath;
        _log = log;
        _config = DesktopConfig.Load(_path);

        Text = "配置";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1501), Theme.S(1036));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        BuildConfigGrid();
        BuildAddressGrid(context);
        BuildToolbar();
        Controls.Add(_grid);
        Controls.Add(_addresses);
    }

    private void BuildConfigGrid()
    {
        _grid.Name = "sgConfig";
        _grid.Left = 0;
        _grid.Top = 0;
        _grid.Width = Theme.S(761);
        _grid.Height = Theme.S(998);
        _grid.Font = Theme.F(24, true, "新宋体");
        _grid.BackgroundColor = SystemColors.Window;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = Theme.S(26);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);

        _grid.Columns.Add("Section", "节");
        _grid.Columns.Add("Key", "键");
        _grid.Columns.Add("Value", "值");
        _grid.Columns[0].ReadOnly = true;
        _grid.Columns[0].FillWeight = 100;
        _grid.Columns[1].ReadOnly = true;
        _grid.Columns[1].FillWeight = 140;
        _grid.Columns[2].FillWeight = 300;
        _grid.Columns[2].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 230);

        LoadConfigRows();

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 2)
            {
                _grid.BeginEdit(true);
            }
        };

        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2)
            {
                return;
            }

            var section = _grid.Rows[e.RowIndex].Cells[0].Value as string;
            var key = _grid.Rows[e.RowIndex].Cells[1].Value as string;
            var value = _grid.Rows[e.RowIndex].Cells[2].Value as string ?? string.Empty;
            if (section is null || key is null)
            {
                return;
            }

            _config.Set(section, key, value);
            try
            {
                _config.Save(_path);
                _log.Append($"配置已保存：{section}/{key}={value}");
                _status.Text = $"已保存 {DateTime.Now:HH:mm:ss}  {section}/{key}={value}";
            }
            catch (Exception ex)
            {
                _log.Append($"配置保存失败：{ex.Message}");
                _status.Text = $"保存失败：{ex.Message}";
            }
        };

        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var section = _grid.Rows[e.RowIndex].Cells[0].Value as string;
            var key = _grid.Rows[e.RowIndex].Cells[1].Value as string;
            _status.Text = $"{section} / {key}";
        };
    }

    private void LoadConfigRows()
    {
        _grid.Rows.Clear();
        foreach (var (section, key, value) in _config.Entries())
        {
            _grid.Rows.Add(section, key, value);
        }
    }

    private void BuildAddressGrid(ReplicaContext context)
    {
        _addresses.Name = "sgHQAddress";
        _addresses.Left = Theme.S(765);
        _addresses.Top = 0;
        _addresses.Width = Theme.S(736);
        _addresses.Height = Theme.S(998);
        _addresses.Font = Theme.F(24, true, "新宋体");
        _addresses.BackgroundColor = SystemColors.Window;
        _addresses.RowHeadersVisible = false;
        _addresses.AllowUserToAddRows = false;
        _addresses.AllowUserToDeleteRows = false;
        _addresses.ReadOnly = true;
        _addresses.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _addresses.RowTemplate.Height = Theme.S(26);
        _addresses.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _addresses.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);

        _addresses.Columns.Add("Index", "序号");
        _addresses.Columns.Add("User", "用户");
        _addresses.Columns.Add("Market", "行情地址");
        _addresses.Columns.Add("Trade", "交易地址");
        _addresses.Columns[0].FillWeight = 40;

        var index = 1;
        foreach (var account in context.Accounts)
        {
            _addresses.Rows.Add(
                index++,
                string.IsNullOrWhiteSpace(account.DisplayName) ? account.UserId : account.DisplayName,
                string.IsNullOrWhiteSpace(account.MarketFront) ? "-" : account.MarketFront,
                string.IsNullOrWhiteSpace(account.TradeFront) ? "-" : account.TradeFront);
        }
    }

    private void BuildToolbar()
    {
        var top = Theme.S(999);

        _search.Left = Theme.S(4);
        _search.Top = top + Theme.S(4);
        _search.Width = Theme.S(260);
        _search.Height = Theme.S(28);
        _search.Font = Theme.F(20, false, "新宋体");
        _search.ForeColor = Color.Maroon;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.PlaceholderText = "过滤 节 / 键";
        _search.TextChanged += (_, _) => ApplyFilter();

        _status.Left = Theme.S(272);
        _status.Top = top;
        _status.Width = Theme.S(560);
        _status.Height = Theme.S(34);
        _status.AutoSize = false;
        _status.Font = Theme.F(18, false, "新宋体");
        _status.ForeColor = Color.Teal;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = $"共 {_grid.Rows.Count} 项配置";

        _pathLabel.Left = Theme.S(830);
        _pathLabel.Top = top;
        _pathLabel.Width = Theme.S(421);
        _pathLabel.Height = Theme.S(34);
        _pathLabel.AutoSize = false;
        _pathLabel.Font = Theme.F(16, false, "新宋体");
        _pathLabel.ForeColor = Color.DimGray;
        _pathLabel.TextAlign = ContentAlignment.MiddleRight;
        _pathLabel.Text = _path;
        var tip = new ToolTip();
        tip.SetToolTip(_pathLabel, _path);

        var reload = Theme.MakeButton(this, "BtReload", Theme.S(1260), top + Theme.S(4), Theme.S(75),
            Theme.S(28), "重载", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        reload.Click += (_, _) =>
        {
            _config = DesktopConfig.Load(_path);
            LoadConfigRows();
            ApplyFilter();
            _status.Text = $"已重载 {DateTime.Now:HH:mm:ss}";
            _log.Append("配置已从磁盘重载。");
        };

        var open = Theme.MakeButton(this, "BtOpenFolder", Theme.S(1340), top + Theme.S(4), Theme.S(75),
            Theme.S(28), "目录", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        open.Click += (_, _) =>
        {
            try
            {
                var folder = Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory;
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Append($"打开目录失败：{ex.Message}");
            }
        };

        var close = Theme.MakeButton(this, "BtClose", Theme.S(1420), top + Theme.S(4), Theme.S(75),
            Theme.S(28), "关闭", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        close.Click += (_, _) => Close();

        Controls.AddRange([_search, _status, _pathLabel, reload, open, close]);
    }

    private void ApplyFilter()
    {
        var keyword = _search.Text.Trim();
        var visible = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var section = row.Cells[0].Value as string ?? string.Empty;
            var key = row.Cells[1].Value as string ?? string.Empty;
            var match = keyword.Length == 0 ||
                        section.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        key.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            row.Visible = match;
            if (match)
            {
                visible++;
            }
        }

        _status.Text = $"共 {_grid.Rows.Count} 项配置，显示 {visible} 项";
    }
}
