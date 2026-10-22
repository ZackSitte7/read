namespace Replica.Desktop;

/// <summary>
/// 原软件 TConfigINI 的复刻：1501×998、bsSingle、poScreenCenter。
/// 左侧 sgConfig（3 列）显示并编辑 config.ini，右侧 sgHQAddress（4 列）
/// 列出各账号的行情/交易前置。原软件用 TStringGrid 就地编辑，这里用
/// DataGridView 保持同样的「双击单元格修改」行为。
/// </summary>
internal sealed class ConfigForm : Form
{
    private readonly DesktopConfig _config;
    private readonly string _path;
    private readonly LogHub _log;
    private readonly DataGridView _grid = new();
    private readonly DataGridView _addresses = new();

    internal ConfigForm(ReplicaContext context, LogHub log)
    {
        _path = context.ConfigPath;
        _log = log;
        _config = DesktopConfig.Load(_path);

        Text = "配置";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1501), Theme.S(998));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        BuildConfigGrid();
        BuildAddressGrid(context);
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

        _grid.Columns.Add("Section", "节");
        _grid.Columns.Add("Key", "键");
        _grid.Columns.Add("Value", "值");
        _grid.Columns[0].ReadOnly = true;
        _grid.Columns[1].ReadOnly = true;
        _grid.Columns[2].FillWeight = 300;

        foreach (var (section, key, value) in _config.Entries())
        {
            _grid.Rows.Add(section, key, value);
        }

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
            }
            catch (Exception ex)
            {
                _log.Append($"配置保存失败：{ex.Message}");
            }
        };
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
                account.MarketFront,
                account.TradeFront);
        }
    }
}
