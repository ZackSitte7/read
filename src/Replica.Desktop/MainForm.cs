using Replica.Core.Models;
using Replica.Core.Services;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Replica.Desktop;

/// <summary>
/// 主窗体，对应原软件 TYYWIN：行情表、下单面板、委托、成交、持仓与日志。
/// 复刻版不包含授权状态栏、云端心跳与 MAC 校验。
/// </summary>
public sealed class MainForm : Form
{
    private readonly TradingSession _session;
    private readonly InstrumentRepository _instruments;

    private readonly DataGridView _quoteGrid = CreateGrid(
        ("合约", "InstrumentId", 100),
        ("最新", "LastPrice", 80),
        ("买价", "BidPrice", 80),
        ("买量", "BidVolume", 60),
        ("卖价", "AskPrice", 80),
        ("卖量", "AskVolume", 60),
        ("成交量", "Volume", 80),
        ("持仓量", "OpenInterest", 80),
        ("更新时间", "UpdateTime", 90));

    private readonly DataGridView _orderGrid = CreateGrid(
        ("委托号", "OrderId", 110),
        ("合约", "InstrumentId", 90),
        ("方向", "Direction", 55),
        ("开平", "Offset", 55),
        ("类型", "OrderType", 55),
        ("价格", "Price", 80),
        ("数量", "Volume", 55),
        ("已成", "TradedVolume", 55),
        ("状态", "Status", 130),
        ("时间", "InsertTime", 80));

    private readonly DataGridView _tradeGrid = CreateGrid(
        ("成交号", "TradeId", 100),
        ("委托号", "OrderId", 110),
        ("合约", "InstrumentId", 90),
        ("方向", "Direction", 55),
        ("开平", "Offset", 55),
        ("成交价", "Price", 80),
        ("手数", "Volume", 55),
        ("时间", "TradeTime", 80));

    private readonly DataGridView _positionGrid = CreateGrid(
        ("合约", "InstrumentId", 100),
        ("方向", "Direction", 60),
        ("手数", "Volume", 60),
        ("开仓均价", "AvgPrice", 90),
        ("最新价", "LastPrice", 90),
        ("浮动盈亏", "UnrealizedPnl", 90));

    private readonly ComboBox _instrumentInput = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDown,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly ComboBox _directionInput = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly ComboBox _offsetInput = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly ComboBox _typeInput = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly NumericUpDown _priceInput = new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 2,
        Maximum = 10_000_000,
        Increment = 1,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly NumericUpDown _volumeInput = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 1,
        Maximum = 1000,
        Value = 1,
        Margin = new Padding(0, 4, 0, 4),
    };

    private readonly Button _placeButton = new()
    {
        Text = "下单",
        Dock = DockStyle.Fill,
        Height = 36,
        BackColor = Color.FromArgb(0, 120, 212),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };

    private readonly Button _cancelButton = new()
    {
        Text = "撤销选中委托",
        Dock = DockStyle.Fill,
        Height = 32,
    };

    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9F),
    };

    private readonly ToolStripStatusLabel _connectionLabel = new("未连接");
    private readonly ToolStripStatusLabel _accountLabel = new(string.Empty);
    private readonly ToolStripStatusLabel _clockLabel = new(string.Empty);

    private readonly WinFormsTimer _clock = new() { Interval = 1000 };

    private SplitContainer? _split;

    private readonly Dictionary<string, int> _quoteRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _orderRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _positionRows = new(StringComparer.OrdinalIgnoreCase);

    private bool _closing;

    public MainForm(TradingSession session, InstrumentRepository instruments)
    {
        _session = session;
        _instruments = instruments;

        Text = "期货交易终端 · 复刻版";
        ClientSize = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);

        _directionInput.Items.AddRange(["买", "卖"]);
        _directionInput.SelectedIndex = 0;
        _offsetInput.Items.AddRange(["开仓", "平仓"]);
        _offsetInput.SelectedIndex = 0;
        _typeInput.Items.AddRange(["限价", "市价"]);
        _typeInput.SelectedIndex = 0;

        BuildLayout();
        BindSessionEvents();

        _clock.Tick += (_, _) => _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clock.Start();
        _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (_session.Account is { } account)
        {
            _accountLabel.Text = $"{account.BrokerId}/{account.UserId}";
        }

        InitializeQuoteRows();
        UpdatePriceFromQuote();
        Shown += async (_, _) => await RefreshPositionsAsync();
    }

    private void BuildLayout()
    {
        var menu = new MenuStrip();
        var systemMenu = new ToolStripMenuItem("系统(&S)");
        var exitItem = new ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => Close();
        systemMenu.DropDownItems.Add(exitItem);
        menu.Items.Add(systemMenu);

        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        var aboutItem = new ToolStripMenuItem("关于复刻版");
        aboutItem.Click += (_, _) => ShowAbout();
        helpMenu.DropDownItems.Add(aboutItem);
        menu.Items.Add(helpMenu);

        var quoteArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
        };
        quoteArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        quoteArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        quoteArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var quotePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        var quoteHeader = new Label
        {
            Text = "行情",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        quotePanel.Controls.Add(_quoteGrid);
        quotePanel.Controls.Add(quoteHeader);
        quoteArea.Controls.Add(quotePanel, 0, 0);

        var orderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(248, 249, 250),
        };
        orderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        orderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (var i = 0; i < 6; i++)
        {
            orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }

        var orderTitle = new Label
        {
            Text = "下单",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        orderPanel.Controls.Add(orderTitle, 0, 0);
        orderPanel.SetColumnSpan(orderTitle, 2);

        AddOrderRow(orderPanel, 1, "合约", _instrumentInput);
        AddOrderRow(orderPanel, 2, "方向", _directionInput);
        AddOrderRow(orderPanel, 3, "开平", _offsetInput);
        AddOrderRow(orderPanel, 4, "类型", _typeInput);
        AddOrderRow(orderPanel, 5, "价格", _priceInput);
        AddOrderRow(orderPanel, 6, "数量", _volumeInput);

        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _placeButton.FlatAppearance.BorderSize = 0;
        orderPanel.Controls.Add(_placeButton, 0, 7);
        orderPanel.SetColumnSpan(_placeButton, 2);

        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        orderPanel.Controls.Add(_cancelButton, 0, 8);
        orderPanel.SetColumnSpan(_cancelButton, 2);

        var hint = new Label
        {
            Text = "复刻版：无授权码、无 MAC 绑定、无云端认证。",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            AutoSize = false,
        };
        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        orderPanel.Controls.Add(hint, 0, 9);
        orderPanel.SetColumnSpan(hint, 2);

        quoteArea.Controls.Add(orderPanel, 1, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateTab("委托", _orderGrid));
        tabs.TabPages.Add(CreateTab("成交", _tradeGrid));
        tabs.TabPages.Add(CreateTab("持仓", _positionGrid));
        tabs.TabPages.Add(CreateTab("日志", _logBox));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 4,
        };
        split.Panel1.Controls.Add(quoteArea);
        split.Panel2.Controls.Add(tabs);

        var status = new StatusStrip();
        status.Items.Add(_connectionLabel);
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(_accountLabel);
        status.Items.Add(new ToolStripStatusLabel { Width = 12 });
        status.Items.Add(_clockLabel);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(menu);

        _split = split;

        _placeButton.Click += OnPlaceOrderClick;
        _cancelButton.Click += OnCancelOrderClick;
        _instrumentInput.SelectedIndexChanged += (_, _) => UpdatePriceFromQuote();
        _typeInput.SelectedIndexChanged += (_, _) =>
            _priceInput.Enabled = _typeInput.SelectedIndex == 0;
        _quoteGrid.CellDoubleClick += OnQuoteDoubleClick;
    }

    private static void AddOrderRow(
        TableLayoutPanel layout,
        int row,
        string caption,
        Control control)
    {
        layout.Controls.Add(
            new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            },
            0,
            row);
        layout.Controls.Add(control, 1, row);
    }

    private static TabPage CreateTab(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(2) };
        page.Controls.Add(content);
        return page;
    }

    private static DataGridView CreateGrid(
        params (string Header, string Name, int Weight)[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        foreach (var (header, name, weight) in columns)
        {
            grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = header,
                    Name = name,
                    FillWeight = weight,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                });
        }

        return grid;
    }

    private void BindSessionEvents()
    {
        _session.QuoteUpdated += OnQuoteUpdated;
        _session.OrderUpdated += OnOrderUpdated;
        _session.TradeReceived += OnTradeReceived;
        _session.PositionUpdated += OnPositionUpdated;
        _session.ConnectionChanged += OnConnectionChanged;
        _session.Log += (_, message) => AppendLog(message);
    }

    private void InitializeQuoteRows()
    {
        foreach (var id in _session.Subscriptions.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var index = _quoteGrid.Rows.Add();
            _quoteGrid.Rows[index].Cells["InstrumentId"].Value = id;
            _quoteRows[id] = index;
            _instrumentInput.Items.Add(id);
        }

        if (_instrumentInput.Items.Count > 0)
        {
            _instrumentInput.SelectedIndex = 0;
        }
    }

    private void OnQuoteUpdated(object? sender, Quote quote) =>
        RunOnUi(() =>
        {
            if (!_quoteRows.TryGetValue(quote.InstrumentId, out var index))
            {
                index = _quoteGrid.Rows.Add();
                _quoteRows[quote.InstrumentId] = index;
                _quoteGrid.Rows[index].Cells["InstrumentId"].Value = quote.InstrumentId;
            }

            var row = _quoteGrid.Rows[index];
            row.Cells["LastPrice"].Value = quote.LastPrice.ToString("0.##");
            row.Cells["BidPrice"].Value = quote.BidPrice.ToString("0.##");
            row.Cells["BidVolume"].Value = quote.BidVolume;
            row.Cells["AskPrice"].Value = quote.AskPrice.ToString("0.##");
            row.Cells["AskVolume"].Value = quote.AskVolume;
            row.Cells["Volume"].Value = quote.Volume;
            row.Cells["OpenInterest"].Value = quote.OpenInterest.ToString("0.##");
            row.Cells["UpdateTime"].Value = quote.UpdateTime.ToString("HH:mm:ss");

            if (string.Equals(_instrumentInput.Text, quote.InstrumentId, StringComparison.OrdinalIgnoreCase) &&
                !_priceInput.Focused)
            {
                SetPrice(quote.LastPrice);
            }

            if (_positionRows.Count > 0)
            {
                RefreshPositionPrices(quote);
            }
        });

    private void OnOrderUpdated(object? sender, Order order) =>
        RunOnUi(() =>
        {
            if (!_orderRows.TryGetValue(order.OrderId, out var index))
            {
                index = _orderGrid.Rows.Add();
                _orderRows[order.OrderId] = index;
            }

            var row = _orderGrid.Rows[index];
            row.Cells["OrderId"].Value = order.OrderId;
            row.Cells["InstrumentId"].Value = order.InstrumentId;
            row.Cells["Direction"].Value = DirectionText(order.Direction);
            row.Cells["Offset"].Value = OffsetText(order.Offset);
            row.Cells["OrderType"].Value = TypeText(order.Type);
            row.Cells["Price"].Value = order.Type == OrderType.Market
                ? "市价"
                : order.Price.ToString("0.##");
            row.Cells["Volume"].Value = order.Volume;
            row.Cells["TradedVolume"].Value = order.TradedVolume;
            row.Cells["Status"].Value = string.IsNullOrEmpty(order.StatusMessage)
                ? StatusText(order.Status)
                : $"{StatusText(order.Status)} · {order.StatusMessage}";
            row.Cells["InsertTime"].Value = order.InsertTime.ToString("HH:mm:ss");
        });

    private void OnTradeReceived(object? sender, Trade trade) =>
        RunOnUi(() =>
        {
            var index = _tradeGrid.Rows.Add();
            var row = _tradeGrid.Rows[index];
            row.Cells["TradeId"].Value = trade.TradeId;
            row.Cells["OrderId"].Value = trade.OrderId;
            row.Cells["InstrumentId"].Value = trade.InstrumentId;
            row.Cells["Direction"].Value = DirectionText(trade.Direction);
            row.Cells["Offset"].Value = OffsetText(trade.Offset);
            row.Cells["Price"].Value = trade.Price.ToString("0.##");
            row.Cells["Volume"].Value = trade.Volume;
            row.Cells["TradeTime"].Value = trade.TradeTime.ToString("HH:mm:ss");
        });

    private void OnPositionUpdated(object? sender, Position position) =>
        RunOnUi(() => UpdatePositionRow(position));

    private void OnConnectionChanged(object? sender, ConnectionState state) =>
        RunOnUi(() => _connectionLabel.Text = ConnectionText(state));

    private void UpdatePositionRow(Position position)
    {
        var key = $"{position.InstrumentId}|{position.Direction}";
        if (!_positionRows.TryGetValue(key, out var index))
        {
            index = _positionGrid.Rows.Add();
            _positionRows[key] = index;
            _positionGrid.Rows[index].Cells["InstrumentId"].Value = position.InstrumentId;
            _positionGrid.Rows[index].Cells["Direction"].Value = PositionText(position.Direction);
        }

        var lastPrice = _session.GetQuote(position.InstrumentId)?.LastPrice ?? position.LastPrice;
        var pnl = position.Direction == PositionDirection.Long
            ? (lastPrice - position.AvgPrice) * position.Volume
            : (position.AvgPrice - lastPrice) * position.Volume;

        var row = _positionGrid.Rows[index];
        row.Cells["Volume"].Value = position.Volume;
        row.Cells["AvgPrice"].Value = position.AvgPrice.ToString("0.##");
        row.Cells["LastPrice"].Value = lastPrice.ToString("0.##");
        row.Cells["UnrealizedPnl"].Value = pnl.ToString("0.##");
        row.DefaultCellStyle.ForeColor = pnl < 0 ? Color.Firebrick : Color.Black;
    }

    private void RefreshPositionPrices(Quote quote)
    {
        foreach (var pair in _positionRows)
        {
            var row = _positionGrid.Rows[pair.Value];
            if (!string.Equals(
                    row.Cells["InstrumentId"].Value?.ToString(),
                    quote.InstrumentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var volume = Convert.ToInt32(row.Cells["Volume"].Value ?? 0);
            var avgPrice = Convert.ToDouble(row.Cells["AvgPrice"].Value ?? "0");
            var isLong = string.Equals(row.Cells["Direction"].Value?.ToString(), "多", StringComparison.Ordinal);
            var pnl = isLong
                ? (quote.LastPrice - avgPrice) * volume
                : (avgPrice - quote.LastPrice) * volume;

            row.Cells["LastPrice"].Value = quote.LastPrice.ToString("0.##");
            row.Cells["UnrealizedPnl"].Value = pnl.ToString("0.##");
            row.DefaultCellStyle.ForeColor = pnl < 0 ? Color.Firebrick : Color.Black;
        }
    }

    private async Task RefreshPositionsAsync()
    {
        try
        {
            var positions = await _session.QueryPositionsAsync();
            RunOnUi(() =>
            {
                foreach (var position in positions)
                {
                    UpdatePositionRow(position);
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"查询持仓失败：{ex.Message}");
        }
    }

    private async void OnPlaceOrderClick(object? sender, EventArgs e)
    {
        _placeButton.Enabled = false;
        try
        {
            var instrumentId = _instrumentInput.Text.Trim();
            if (instrumentId.Length == 0)
            {
                throw new InvalidOperationException("请输入合约代码");
            }

            if (!_session.Subscriptions.Contains(instrumentId, StringComparer.OrdinalIgnoreCase))
            {
                await _session.SubscribeAsync([instrumentId]);
                RunOnUi(() =>
                {
                    if (!_instrumentInput.Items.Contains(instrumentId))
                    {
                        _instrumentInput.Items.Add(instrumentId);
                    }
                });
            }

            var type = _typeInput.SelectedIndex == 1 ? OrderType.Market : OrderType.Limit;
            var request = new OrderRequest
            {
                InstrumentId = instrumentId,
                Direction = _directionInput.SelectedIndex == 1
                    ? OrderDirection.Sell
                    : OrderDirection.Buy,
                Offset = _offsetInput.SelectedIndex == 1 ? OffsetFlag.Close : OffsetFlag.Open,
                Type = type,
                Price = type == OrderType.Market ? 0 : (double)_priceInput.Value,
                Volume = (int)_volumeInput.Value,
            };

            var order = await _session.PlaceOrderAsync(request);
            AppendLog($"委托 {order.OrderId} 已提交：{order.StatusMessage}");
        }
        catch (Exception ex)
        {
            AppendLog($"下单失败：{ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "下单失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _placeButton.Enabled = true;
        }
    }

    private async void OnCancelOrderClick(object? sender, EventArgs e)
    {
        if (_orderGrid.SelectedRows.Count == 0)
        {
            AppendLog("请先在委托列表中选择要撤销的委托");
            return;
        }

        var orderId = _orderGrid.SelectedRows[0].Cells["OrderId"].Value?.ToString();
        if (string.IsNullOrEmpty(orderId))
        {
            return;
        }

        try
        {
            await _session.CancelOrderAsync(orderId);
            AppendLog($"已发送撤单请求：{orderId}");
        }
        catch (Exception ex)
        {
            AppendLog($"撤单失败：{ex.Message}");
        }
    }

    private void OnQuoteDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        var instrumentId = _quoteGrid.Rows[e.RowIndex].Cells["InstrumentId"].Value?.ToString();
        if (string.IsNullOrEmpty(instrumentId))
        {
            return;
        }

        _instrumentInput.Text = instrumentId;
        UpdatePriceFromQuote();
    }

    private void UpdatePriceFromQuote()
    {
        var instrumentId = _instrumentInput.Text.Trim();
        if (instrumentId.Length == 0)
        {
            return;
        }

        var instrument = _instruments.Find(instrumentId);
        if (instrument is { PriceTick: > 0 })
        {
            _priceInput.Increment = (decimal)instrument.PriceTick;
        }

        var quote = _session.GetQuote(instrumentId);
        if (quote is not null)
        {
            SetPrice(quote.LastPrice);
        }
    }

    private void SetPrice(double price)
    {
        var value = (decimal)price;
        _priceInput.Value = Math.Clamp(value, _priceInput.Minimum, _priceInput.Maximum);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        RunOnUi(() =>
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"));
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "期货交易终端复刻版\n\n" +
            "按原软件架构重建登录、行情、下单、成交与持仓链路。\n" +
            "本版本不包含厂商授权码、MAC 绑定、云端认证与版本校验。",
            "关于复刻版",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RunOnUi(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // 窗体正在销毁。
            }

            return;
        }

        action();
    }

    private static string DirectionText(OrderDirection direction) =>
        direction == OrderDirection.Buy ? "买" : "卖";

    private static string OffsetText(OffsetFlag offset) => offset switch
    {
        OffsetFlag.Open => "开仓",
        OffsetFlag.Close => "平仓",
        OffsetFlag.CloseToday => "平今",
        _ => "平昨",
    };

    private static string TypeText(OrderType type) =>
        type == OrderType.Market ? "市价" : "限价";

    private static string PositionText(PositionDirection direction) =>
        direction == PositionDirection.Long ? "多" : "空";

    private static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "未成交",
        OrderStatus.PartiallyFilled => "部分成交",
        OrderStatus.Filled => "全部成交",
        OrderStatus.Cancelled => "已撤单",
        _ => "已拒绝",
    };

    private static string ConnectionText(ConnectionState state) => state switch
    {
        ConnectionState.Connecting => "连接中",
        ConnectionState.Connected => "已连接",
        ConnectionState.Faulted => "连接异常",
        _ => "未连接",
    };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_split is not null && _split.Height > 480)
        {
            _split.SplitterDistance = 360;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _clock.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.QuoteUpdated -= OnQuoteUpdated;
        _session.OrderUpdated -= OnOrderUpdated;
        _session.TradeReceived -= OnTradeReceived;
        _session.PositionUpdated -= OnPositionUpdated;
        _session.ConnectionChanged -= OnConnectionChanged;

        try
        {
            _session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // 退出时忽略清理异常。
        }

        base.OnFormClosed(e);
    }
}
