using Replica.Core.Models;

namespace Replica.Desktop;

/// <summary>
/// 原软件 TPointWindow 的复刻：无边框、置顶、可拖动的浮条。
/// 收起时只显示账户与盈亏；双击（或「全部」）展开为分组/搜索/A-B 腿/白格/切组/撤组/仓/平 操作条。
/// </summary>
internal sealed class PointWindow : Form
{
    private readonly MainForm _main;
    private readonly ReplicaContext _ctx;
    private readonly Label _lbUser = new();
    private readonly Label _lbYk = new();
    private readonly Label _lbRyk = new();
    private readonly CheckBox[] _groups = new CheckBox[20];
    private readonly List<Control> _expandedControls = [];
    private readonly RadioButton _rboDan = new();
    private readonly RadioButton _rboDuo = new();
    private readonly CheckBox _cbBg = new();
    private readonly CheckBox _cbHotGrop = new();
    private readonly CheckBox _cbZcd = new();
    private readonly CheckBox _cbFlag = new();
    private readonly TextBox _search = new();
    private readonly Button _btGroupAll = new();
    private readonly Button _btMode = new();
    private readonly Button _btTile2 = new();
    private readonly Button _btPosition = new();
    private readonly Button _btCloseRecord = new();
    private readonly Button _btLegA = new();
    private readonly Button _btLegB = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly ToolTip _tip = new();

    private bool _expanded;
    private bool _twoRows = true;
    private bool _dragging;
    private Point _dragOffset;

    internal PointWindow(MainForm main, ReplicaContext context)
    {
        _main = main;
        _ctx = context;

        Text = "点价";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Theme.S(47), Theme.S(31));
        BackColor = Theme.Delphi(14200751);
        Font = Theme.F(17, false, "MS Sans Serif");

        BuildControls();
        ApplyState();

        _refresh.Tick += (_, _) => RefreshPnl();
        _refresh.Start();

        MouseDown += DragStart;
        MouseMove += DragMove;
        MouseUp += DragEnd;
        DoubleClick += (_, _) => ToggleExpanded();
        ContextMenuStrip = BuildMenu();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _expanded)
            {
                ToggleExpanded();
                e.Handled = true;
            }
        };
        FormClosed += (_, _) =>
        {
            _refresh.Dispose();
            _tip.Dispose();
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Font = Theme.F(17, false, "微软雅黑") };
        var expand = menu.Items.Add("展开 / 收起");
        expand.Click += (_, _) => ToggleExpanded();
        var top = (ToolStripMenuItem)menu.Items.Add("窗口置顶");
        top.CheckOnClick = true;
        top.Checked = TopMost;
        top.Click += (_, _) => TopMost = top.Checked;
        menu.Items.Add(new ToolStripSeparator());
        var positions = menu.Items.Add("持仓列表");
        positions.Click += (_, _) => ShowPositions();
        var closeRecord = menu.Items.Add("平仓记录");
        closeRecord.Click += (_, _) => _main.ShowCloseRecords();
        menu.Items.Add(new ToolStripSeparator());
        var hide = menu.Items.Add("隐藏");
        hide.Click += (_, _) => Hide();
        return menu;
    }

    private void BuildControls()
    {
        MakeLabel(_lbUser, "LBUser", 0, 1, 180, 26, Theme.F(24, true, "宋体"), Color.Fuchsia,
            ContentAlignment.MiddleCenter);
        _lbUser.Text = "未登录";
        _lbUser.DoubleClick += (_, _) => ToggleExpanded();
        HookDrag(_lbUser);
        _expandedControls.Remove(_lbUser);

        MakeLabel(_lbRyk, "LBRYK", 10, 31, 216, 26, Theme.F(20, true, "宋体"), Theme.Delphi(64),
            ContentAlignment.MiddleCenter);
        HookDrag(_lbRyk);

        MakeLabel(_lbYk, "LBYK", 12, 60, 1166, 30, Theme.F(26, true, "宋体"), Theme.Delphi(200),
            ContentAlignment.MiddleCenter);
        HookDrag(_lbYk);

        MakeButton(_btGroupAll, "BtGroupAll", 876, 1, 45, 28, "全部", Color.Maroon);
        _btGroupAll.Click += (_, _) =>
        {
            foreach (var box in _groups)
            {
                box.Checked = true;
            }

            if (!_expanded)
            {
                ToggleExpanded();
            }
            else
            {
                _main.SetGroup(1);
            }
        };

        _search.Name = "TxHyChaXun";
        _search.Left = Theme.S(1028);
        _search.Top = Theme.S(-1);
        _search.Width = Theme.S(150);
        _search.Height = Theme.S(28);
        _search.Font = Theme.F(20, false, "新宋体");
        _search.Text = "搜索";
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.MaxLength = 24;
        _search.Click += (_, _) =>
        {
            if (_search.Text == "搜索")
            {
                _search.Clear();
            }
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            var keyword = _search.Text.Trim();
            var match = _ctx.Instruments.All.FirstOrDefault(i =>
                string.Equals(i.InstrumentId, keyword, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.Name, keyword, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _ctx.Log.Append($"点价窗搜索：未找到 {keyword}。");
            }
            else
            {
                _main.ActivateInstrument(match.InstrumentId);
                _ctx.Log.Append($"点价窗搜索：切换到 {match.InstrumentId}。");
            }

            e.SuppressKeyPress = true;
        };
        Controls.Add(_search);
        _expandedControls.Add(_search);

        MakeButton(_btMode, "BtMode", 927, 1, 45, 28, "模式", Color.Maroon);
        _btMode.Click += (_, _) => _main.CyclePriceStep();

        MakeButton(_btTile2, "BtTile2", 978, 1, 45, 28, "两排", Color.Maroon);
        _btTile2.Click += (_, _) =>
        {
            _twoRows = !_twoRows;
            ApplyState();
        };

        _cbFlag.Name = "CBcjflag";
        _cbFlag.Text = "标识";
        _cbFlag.Left = Theme.S(910);
        _cbFlag.Top = Theme.S(31);
        _cbFlag.Width = Theme.S(68);
        _cbFlag.Height = Theme.S(23);
        _cbFlag.Font = Theme.F(17, false, "微软雅黑");
        _cbFlag.ForeColor = Color.Olive;
        _cbFlag.BackColor = BackColor;
        _cbFlag.Click += (_, _) => _main.ToggleTradeFlag();
        AddExpanded(_cbFlag);

        MakeButton(_btLegB, "BtRBOB", 1150, 31, 28, 28, "B", Color.Maroon);
        _btLegB.Click += (_, _) => _main.SelectLeg(false);

        MakeButton(_btLegA, "BtRBOA", 1115, 31, 29, 28, "A", Color.Maroon);
        _btLegA.Click += (_, _) => _main.SelectLeg(true);

        for (var i = 0; i < _groups.Length; i++)
        {
            var index = i;
            var box = _groups[i];
            box.Name = $"CBGroup{i + 1}";
            box.Text = (i + 1).ToString();
            box.Checked = i < 10;
            box.Left = Theme.S(234 + (i % 10) * 51);
            box.Top = Theme.S(i < 10 ? 3 : 31);
            box.Width = Theme.S(50);
            box.Height = Theme.S(22);
            box.Font = Theme.F(17, false, "微软雅黑");
            box.ForeColor = Color.Maroon;
            box.BackColor = BackColor;
            box.Click += (_, _) =>
            {
                if (box.Checked)
                {
                    _main.SetGroup(index + 1);
                }
            };
            AddExpanded(box);
        }

        _rboDan.Name = "RBODan";
        _rboDan.Text = "单";
        _rboDan.Left = Theme.S(770);
        _rboDan.Top = Theme.S(2);
        _rboDan.Width = Theme.S(47);
        _rboDan.Height = Theme.S(25);
        _rboDan.Checked = true;
        _rboDan.Font = Theme.F(17, false, "微软雅黑");
        _rboDan.BackColor = BackColor;
        AddExpanded(_rboDan);

        _rboDuo.Name = "RBODuo";
        _rboDuo.Text = "多";
        _rboDuo.Left = Theme.S(823);
        _rboDuo.Top = Theme.S(2);
        _rboDuo.Width = Theme.S(47);
        _rboDuo.Height = Theme.S(25);
        _rboDuo.Font = Theme.F(17, false, "微软雅黑");
        _rboDuo.BackColor = BackColor;
        AddExpanded(_rboDuo);

        MakeButton(_btPosition, "BtPositionWin", 1045, 31, 29, 28, "仓", Color.Maroon);
        _btPosition.Click += (_, _) => ShowPositions();

        _cbBg.Name = "CBBG";
        _cbBg.Text = "白格";
        _cbBg.Left = Theme.S(979);
        _cbBg.Top = Theme.S(31);
        _cbBg.Width = Theme.S(68);
        _cbBg.Height = Theme.S(23);
        _cbBg.Checked = true;
        _cbBg.Font = Theme.F(17, false, "微软雅黑");
        _cbBg.ForeColor = Theme.Delphi(16744576);
        _cbBg.BackColor = BackColor;
        _cbBg.Click += (_, _) => _main.SetWorkingOrdersVisible(_cbBg.Checked);
        AddExpanded(_cbBg);

        _cbHotGrop.Name = "CBHotGrop";
        _cbHotGrop.Text = "切组";
        _cbHotGrop.Left = Theme.S(770);
        _cbHotGrop.Top = Theme.S(31);
        _cbHotGrop.Width = Theme.S(68);
        _cbHotGrop.Height = Theme.S(23);
        _cbHotGrop.Checked = true;
        _cbHotGrop.Font = Theme.F(17, false, "微软雅黑");
        _cbHotGrop.ForeColor = Color.Teal;
        _cbHotGrop.BackColor = BackColor;
        AddExpanded(_cbHotGrop);

        _cbZcd.Name = "CBZCD";
        _cbZcd.Text = "撤组";
        _cbZcd.Left = Theme.S(840);
        _cbZcd.Top = Theme.S(31);
        _cbZcd.Width = Theme.S(68);
        _cbZcd.Height = Theme.S(23);
        _cbZcd.Font = Theme.F(17, false, "微软雅黑");
        _cbZcd.ForeColor = Color.Maroon;
        _cbZcd.BackColor = BackColor;
        _cbZcd.Click += async (_, _) =>
        {
            await _main.CancelGroupOrdersAsync();
            _cbZcd.Checked = false;
        };
        AddExpanded(_cbZcd);

        MakeButton(_btCloseRecord, "BtCloseRecordWin", 1080, 31, 29, 28, "平", Color.Maroon);
        _btCloseRecord.Click += (_, _) => _main.ShowCloseRecords();
    }

    private void MakeLabel(
        Label label,
        string name,
        int x,
        int y,
        int width,
        int height,
        Font font,
        Color fore,
        ContentAlignment align)
    {
        label.Name = name;
        label.AutoSize = false;
        label.Left = Theme.S(x);
        label.Top = Theme.S(y);
        label.Width = Theme.S(width);
        label.Height = Theme.S(height);
        label.Font = font;
        label.ForeColor = fore;
        label.BackColor = BackColor;
        label.TextAlign = align;
        label.UseMnemonic = false;
        Controls.Add(label);
        _expandedControls.Add(label);
    }

    private void MakeButton(
        Button button,
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Color fore)
    {
        button.Name = name;
        button.Text = text;
        button.Left = Theme.S(x);
        button.Top = Theme.S(y);
        button.Width = Theme.S(width);
        button.Height = Theme.S(height);
        button.Font = Theme.F(18, false, "新宋体");
        button.ForeColor = fore;
        button.FlatStyle = FlatStyle.System;
        button.TabStop = false;
        Controls.Add(button);
        _expandedControls.Add(button);
    }

    private void AddExpanded(Control control)
    {
        Controls.Add(control);
        _expandedControls.Add(control);
    }

    private void HookDrag(Control control)
    {
        control.MouseDown += DragStart;
        control.MouseMove += DragMove;
        control.MouseUp += DragEnd;
    }

    private void DragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        var source = (Control?)sender ?? this;
        _dragOffset = source.PointToScreen(e.Location);
        _dragOffset.Offset(-Left, -Top);
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var source = (Control?)sender ?? this;
        var screen = source.PointToScreen(e.Location);
        Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
    }

    private void DragEnd(object? sender, MouseEventArgs e) => _dragging = false;

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        ApplyState();
    }

    private void ApplyState()
    {
        _lbYk.Visible = _expanded;
        _lbRyk.Visible = _expanded;
        foreach (var control in _expandedControls)
        {
            control.Visible = _expanded;
        }

        for (var i = 10; i < _groups.Length; i++)
        {
            _groups[i].Visible = _expanded && _twoRows;
        }

        ClientSize = _expanded
            ? new Size(Theme.S(1180), Theme.S(92))
            : new Size(Theme.S(180), Theme.S(26));

        _lbUser.Width = ClientSize.Width;
        RefreshPnl();
    }

    private void RefreshPnl()
    {
        if (IsDisposed)
        {
            return;
        }

        var summary = _main.PnlSummary();
        _lbUser.Text = summary.Account;
        _lbUser.ForeColor = _main.Session.IsConnected ? Color.Fuchsia : Color.Silver;
        _lbRyk.Text = $"平 {summary.Closed}";
        _lbRyk.ForeColor = PnlColor(summary.Closed);
        _lbYk.Text = $"总 {summary.Total}";
        _lbYk.ForeColor = PnlColor(summary.Total);
        _tip.SetToolTip(
            _lbUser,
            $"账户 {summary.Account}\n浮盈 {summary.Floating}\n平仓 {summary.Closed}\n合计 {summary.Total}\n" +
            (_main.Session.IsConnected ? "行情已连接" : "行情未连接"));
    }

    private static Color PnlColor(string text) =>
        text.TrimStart().StartsWith('-') ? Color.FromArgb(0, 140, 60) : Color.FromArgb(200, 40, 40);

    private void ShowPositions()
    {
        var positions = _main.OpenPositions.ToArray();
        if (positions.Length == 0)
        {
            _ctx.Log.Append("当前没有持仓。");
            return;
        }

        using var window = new PositionListForm(positions, _ctx.Instruments);
        window.ShowDialog(this);
    }
}

/// <summary>点价窗「仓」按钮打开的持仓列表；原软件没有独立 DFM，这里沿用主窗的持仓字段。</summary>
internal sealed class PositionListForm : Form
{
    public PositionListForm(IReadOnlyList<Position> positions, Replica.Core.Services.InstrumentRepository instruments)
    {
        Text = "持仓";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Theme.S(700), Theme.S(450));
        MinimumSize = new Size(Theme.S(520), Theme.S(300));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = Theme.F(18, false, "Segoe UI");
        BackColor = SystemColors.Control;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Theme.GridBack,
            Font = Theme.F(20, true, "新宋体"),
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AllowUserToResizeRows = false,
            RowTemplate = { Height = Theme.S(26) },
        };

        grid.Columns.Add("Id", "合约");
        grid.Columns.Add("Dir", "方向");
        grid.Columns.Add("Volume", "手数");
        grid.Columns.Add("Avg", "开仓均价");
        grid.Columns.Add("Last", "最新价");
        grid.Columns.Add("Pnl", "浮动盈亏");

        double totalPnl = 0;
        var longVolume = 0;
        var shortVolume = 0;

        foreach (var position in positions)
        {
            var multiple = Math.Max(1, instruments.Find(position.InstrumentId)?.VolumeMultiple ?? 1);
            var pnl = position.Direction == PositionDirection.Long
                ? (position.LastPrice - position.AvgPrice) * position.Volume * multiple
                : (position.AvgPrice - position.LastPrice) * position.Volume * multiple;

            totalPnl += pnl;
            if (position.Direction == PositionDirection.Long)
            {
                longVolume += position.Volume;
            }
            else
            {
                shortVolume += position.Volume;
            }

            var index = grid.Rows.Add(
                position.InstrumentId,
                position.Direction == PositionDirection.Long ? "多" : "空",
                position.Volume,
                position.AvgPrice.ToString("0.###"),
                position.LastPrice.ToString("0.###"),
                pnl.ToString("+0.00;-0.00;0.00"));

            var row = grid.Rows[index];
            row.DefaultCellStyle.ForeColor = position.Direction == PositionDirection.Long
                ? Color.FromArgb(140, 20, 20)
                : Color.FromArgb(20, 110, 60);
            row.Cells[5].Style.ForeColor = pnl >= 0 ? Color.Firebrick : Color.SeaGreen;
            row.Cells[5].Style.Font = Theme.F(20, true, "新宋体");
        }

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(30),
            Text = $"共 {positions.Count} 条持仓    多 {longVolume} 手 / 空 {shortVolume} 手    " +
                   $"合计浮动盈亏 {totalPnl:+0.00;-0.00;0.00}",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.F(20, true, "新宋体"),
            ForeColor = totalPnl >= 0 ? Color.Firebrick : Color.SeaGreen,
            BackColor = SystemColors.Control,
            Padding = new Padding(Theme.S(6), 0, 0, 0),
        };

        Controls.Add(grid);
        Controls.Add(footer);
    }
}
