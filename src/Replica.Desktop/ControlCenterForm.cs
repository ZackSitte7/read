using Replica.Core.Models;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Replica.Desktop;

/// <summary>
/// 控制台窗体，按原软件 TYYCtrlWin 的 1268×642 布局重建：
/// 左侧 SYSLOG、右侧合约列表、底部退出/全关/信息/关组/开盘/价差/对齐等按钮行。
/// </summary>
internal sealed class ControlCenterForm : Form
{
    private readonly MainForm _main;
    private readonly ReplicaContext _ctx;
    private readonly ListView _contractList = new();
    private readonly TextBox _syslog = new();
    private readonly TextBox _search = new();
    private readonly RadioButton _byId = new();
    private readonly RadioButton _byName = new();
    private readonly CheckBox _futures = new();
    private readonly CheckBox _options = new();
    private readonly CheckBox _top5 = new();
    private readonly CheckBox _align = new();
    private readonly Label _time = new();
    private Button _market = new();
    private readonly WinFormsTimer _clock = new() { Interval = 500 };
    private readonly List<string> _ids = [];

    private int _sortColumn;
    private bool _sortAscending = true;
    private bool _refreshing;

    internal ControlCenterForm(MainForm main, ReplicaContext context)
    {
        _main = main;
        _ctx = context;

        Text = "控制台";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1268), Theme.S(642));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = Theme.F(18, false, "Segoe UI");

        BuildLayout();
        RefreshList();
        WriteLogSnapshot();

        _ctx.Log.Appended += OnLogAppended;
        _clock.Tick += (_, _) => OnClockTick();
        _clock.Start();
        OnClockTick();
    }

    private void BuildLayout()
    {
        _syslog.Left = 0;
        _syslog.Top = 0;
        _syslog.Width = Theme.S(659);
        _syslog.Height = Theme.S(600);
        _syslog.Multiline = true;
        _syslog.ReadOnly = true;
        _syslog.ScrollBars = ScrollBars.Vertical;
        _syslog.Font = Theme.F(20);
        _syslog.BackColor = Color.White;
        _syslog.BorderStyle = BorderStyle.FixedSingle;

        _contractList.Left = Theme.S(662);
        _contractList.Top = 0;
        _contractList.Width = Theme.S(605);
        _contractList.Height = Theme.S(600);
        _contractList.View = View.Details;
        _contractList.FullRowSelect = true;
        _contractList.GridLines = true;
        _contractList.HideSelection = false;
        _contractList.MultiSelect = false;
        _contractList.Font = Theme.F(24);
        _contractList.Columns.Add("ID", Theme.S(180));
        _contractList.Columns.Add("Name", Theme.S(250));
        _contractList.Columns.Add("成交量", Theme.S(125));
        _contractList.ColumnClick += (_, e) =>
        {
            if (_sortColumn == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }

            RefreshList();
        };
        _contractList.SelectedIndexChanged += (_, _) =>
            _market.Enabled = _contractList.SelectedItems.Count > 0;
        _contractList.DoubleClick += (_, _) => SendToLadder();

        _time.Left = Theme.S(65);
        _time.Top = Theme.S(605);
        _time.Width = Theme.S(120);
        _time.Height = Theme.S(29);
        _time.AutoSize = false;
        _time.Text = "00:00:00";
        _time.Font = Theme.F(28, false, "新宋体");
        _time.ForeColor = Theme.Delphi(16233578);
        _time.TextAlign = ContentAlignment.MiddleLeft;

        var saveExit = Theme.MakeButton(this, "BtSaveExit", 0, Theme.S(605), Theme.S(60), Theme.S(33), "退出",
            Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        saveExit.Click += (_, _) => Close();

        var allClose = Theme.MakeButton(this, "BtAllClose", Theme.S(190), Theme.S(605), Theme.S(60), Theme.S(33),
            "全关", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        allClose.Click += async (_, _) => await _main.CloseAllPositionsAsync();

        var userInfo = Theme.MakeButton(this, "BtUserInfo", Theme.S(260), Theme.S(605), Theme.S(60), Theme.S(33),
            "信息", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        userInfo.Click += (_, _) => ShowAccountInfo();

        var groupClose = Theme.MakeButton(this, "BtGroupClose", Theme.S(330), Theme.S(605), Theme.S(60),
            Theme.S(33), "关组", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        groupClose.Click += async (_, _) => await _main.CloseGroupPositionsAsync();

        var openOrder = Theme.MakeButton(this, "CBMOrder", Theme.S(400), Theme.S(605), Theme.S(60), Theme.S(33),
            "开盘", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        openOrder.Click += async (_, _) => await OpenSelectedAsync();

        var spread = Theme.MakeButton(this, "BtJiacha", Theme.S(470), Theme.S(605), Theme.S(60), Theme.S(33),
            "价差", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        spread.Click += (_, _) => _main.ToggleSpreadMode();

        _market = Theme.MakeButton(this, "BtMarket", Theme.S(613), Theme.S(605), Theme.S(39), Theme.S(33), ">>",
            Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        _market.Enabled = false;
        _market.Click += (_, _) => SendToLadder();

        _align.Left = Theme.S(540);
        _align.Top = Theme.S(606);
        _align.Width = Theme.S(75);
        _align.Height = Theme.S(30);
        _align.Text = "对齐";
        _align.Font = Theme.F(20, false, "新宋体");
        _align.ForeColor = Color.Teal;
        _align.Click += (_, _) =>
        {
            if (_align.Checked)
            {
                _main.AlignWindow();
            }
        };

        _search.Left = Theme.S(661);
        _search.Top = Theme.S(607);
        _search.Width = Theme.S(120);
        _search.Height = Theme.S(28);
        _search.Font = Theme.F(20, false, "新宋体");
        _search.ForeColor = Color.Maroon;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.PlaceholderText = "搜索";
        _search.TextChanged += (_, _) => RefreshList();

        _byId.Left = Theme.S(788);
        _byId.Top = Theme.S(610);
        _byId.Width = Theme.S(55);
        _byId.Height = Theme.S(24);
        _byId.Text = "ID";
        _byId.Checked = true;
        _byId.Font = Theme.F(24, false, "新宋体");
        _byId.ForeColor = Theme.Delphi(16233578);
        _byId.Click += (_, _) => RefreshList();

        _byName.Left = Theme.S(848);
        _byName.Top = Theme.S(610);
        _byName.Width = Theme.S(90);
        _byName.Height = Theme.S(24);
        _byName.Text = "Name";
        _byName.Font = Theme.F(24, false, "新宋体");
        _byName.ForeColor = Theme.Delphi(16233578);
        _byName.Click += (_, _) => RefreshList();

        _futures.Left = Theme.S(935);
        _futures.Top = Theme.S(608);
        _futures.Width = Theme.S(48);
        _futures.Height = Theme.S(30);
        _futures.Text = "货";
        _futures.Checked = true;
        _futures.Font = Theme.F(20, false, "新宋体");
        _futures.ForeColor = Color.Teal;
        _futures.Click += (_, _) => RefreshList();

        _options.Left = Theme.S(985);
        _options.Top = Theme.S(608);
        _options.Width = Theme.S(48);
        _options.Height = Theme.S(30);
        _options.Text = "权";
        _options.Checked = true;
        _options.Font = Theme.F(20, false, "新宋体");
        _options.ForeColor = Color.Teal;
        _options.Click += (_, _) => RefreshList();

        _top5.Left = Theme.S(1045);
        _top5.Top = Theme.S(608);
        _top5.Width = Theme.S(48);
        _top5.Height = Theme.S(30);
        _top5.Text = "5D";
        _top5.Font = Theme.F(20, false, "新宋体");
        _top5.ForeColor = Color.Teal;
        _top5.Click += (_, _) => RefreshList();

        var refreshVol = Theme.MakeButton(this, "BtUpdateInstrumentsVol", Theme.S(1098), Theme.S(605),
            Theme.S(120), Theme.S(33), "刷新成交量", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        refreshVol.Click += (_, _) =>
        {
            RefreshList();
            _main.RefreshVolumeList();
        };

        Controls.AddRange(
        [
            _syslog,
            _contractList,
            _time,
            saveExit,
            allClose,
            userInfo,
            groupClose,
            openOrder,
            spread,
            _align,
            _market,
            _search,
            _byId,
            _byName,
            _futures,
            _options,
            _top5,
            refreshVol,
        ]);
    }

    private void RefreshList()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var selected = SelectedId();
            var keyword = _search.Text.Trim();
            var query = _ctx.Instruments.All.AsEnumerable();

            query = query.Where(instrument =>
                (_futures.Checked && !IsOption(instrument.InstrumentId)) ||
                (_options.Checked && IsOption(instrument.InstrumentId)));

            if (keyword.Length > 0 && keyword != "搜索")
            {
                query = _byName.Checked
                    ? query.Where(i => (i.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    : query.Where(i => i.InstrumentId.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var items = query.ToList();
            if (_top5.Checked)
            {
                items = items
                    .OrderByDescending(i => _main.Session.GetQuote(i.InstrumentId)?.Volume ?? 0)
                    .Take(5)
                    .ToList();
            }

            items = _sortColumn switch
            {
                1 => (_sortAscending
                        ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase))
                    .ThenBy(i => i.InstrumentId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                2 => (_sortAscending
                        ? items.OrderBy(i => _main.Session.GetQuote(i.InstrumentId)?.Volume ?? 0)
                        : items.OrderByDescending(i => _main.Session.GetQuote(i.InstrumentId)?.Volume ?? 0))
                    .ToList(),
                _ => (_sortAscending
                        ? items.OrderBy(i => i.InstrumentId, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.InstrumentId, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
            };

            _contractList.BeginUpdate();
            _contractList.Items.Clear();
            _ids.Clear();
            foreach (var instrument in items)
            {
                _ids.Add(instrument.InstrumentId);
                var item = new ListViewItem(instrument.InstrumentId);
                item.SubItems.Add(instrument.Name ?? string.Empty);
                item.SubItems.Add((_main.Session.GetQuote(instrument.InstrumentId)?.Volume ?? 0).ToString());
                _contractList.Items.Add(item);
            }

            _contractList.EndUpdate();

            if (selected is not null)
            {
                var index = _ids.FindIndex(id => string.Equals(id, selected, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _contractList.Items[index].Selected = true;
                }
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshVolumes()
    {
        for (var i = 0; i < _contractList.Items.Count && i < _ids.Count; i++)
        {
            var volume = _main.Session.GetQuote(_ids[i])?.Volume ?? 0;
            _contractList.Items[i].SubItems[2].Text = volume.ToString();
        }
    }

    private string? SelectedId() =>
        _contractList.SelectedItems.Count > 0 ? _contractList.SelectedItems[0].Text : null;

    private void SendToLadder()
    {
        var id = SelectedId();
        if (id is null)
        {
            return;
        }

        _main.ActivateInstrument(id);
        _main.Log.Append($"控制台：合约 {id} 已送入行情梯。");
    }

    private async Task OpenSelectedAsync()
    {
        var id = SelectedId();
        if (id is null)
        {
            return;
        }

        try
        {
            await _main.Session.PlaceOrderAsync(
                new OrderRequest
                {
                    InstrumentId = id,
                    Direction = OrderDirection.Buy,
                    Offset = OffsetFlag.Open,
                    Type = OrderType.Market,
                    Volume = 1,
                });
            _main.Log.Append($"控制台：{id} 开盘买入 1 手（市价）。");
        }
        catch (Exception ex)
        {
            _main.Log.Append($"控制台开盘失败：{ex.Message}");
        }
    }

    private void ShowAccountInfo()
    {
        var account = _main.Session.Account;
        var quotes = _main.Session.Subscriptions.Count;
        MessageBox.Show(
            this,
            $"账户：{account?.DisplayName}（{account?.BrokerId}/{account?.UserId}）\n" +
            $"行情：{account?.MarketFront}\n交易：{account?.TradeFront}\n" +
            $"订阅合约：{quotes} 个\n" +
            $"连接状态：{(_main.Session.IsConnected ? "已连接" : "未连接")}\n" +
            "复刻版不含授权码、MAC 绑定与云端认证。",
            "账户信息",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void WriteLogSnapshot()
    {
        _syslog.Clear();
        foreach (var line in _ctx.Log.Snapshot())
        {
            _syslog.AppendText(line + Environment.NewLine);
        }
    }

    private void OnLogAppended(object? sender, string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnLogAppended(sender, line));
            return;
        }

        _syslog.AppendText(line + Environment.NewLine);
    }

    private void OnClockTick()
    {
        _time.Text = DateTime.Now.ToString("HH:mm:ss");
        if (!_refreshing)
        {
            RefreshVolumes();
        }
    }

    private static bool IsOption(string instrumentId) =>
        instrumentId.Contains("-C-", StringComparison.OrdinalIgnoreCase) ||
        instrumentId.Contains("-P-", StringComparison.OrdinalIgnoreCase);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _clock.Stop();
        _clock.Dispose();
        _ctx.Log.Appended -= OnLogAppended;
        base.OnFormClosed(e);
    }
}
