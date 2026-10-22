using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Replica.Core.Models;

namespace Replica.Desktop;

internal sealed class LadderCellEventArgs : EventArgs
{
    public required string InstrumentId { get; init; }

    public double Price { get; init; }

    public OrderDirection Direction { get; init; }

    /// <summary>该价位已有本账户挂单（白格）。</summary>
    public bool HasWorkingOrder { get; init; }

    /// <summary>点击位置在竖框内的列序号（0 买量 … 4 卖量）。</summary>
    public int SubColumn { get; init; }
}

/// <summary>
/// 原软件 TYYWin.SGPriceList 的复刻。
/// 每个合约是一个独立的竖框（对应原版一个 YYWin 交易窗口），
/// 框内五列比例取自 config.ini 的 PriceListRatios=10,25,30,25,10：
/// 买量 | 买价 | 最新 | 卖价 | 卖量。
/// 竖框左半边点击做多，右半边点击做空；最左侧细长条用于切换合约列表（分组）。
/// </summary>
internal sealed class PriceLadder : Control
{
    public const int MaxColumns = 5;
    public const int RailWidth = 16;

    // 以下尺寸按 96 DPI 设计，绘制时再按 DeviceDpi 放大。
    private const int BaseHeaderHeight = 36;
    private const int BaseColumnHeaderHeight = 16;
    private const int BaseRailTabHeight = 24;

    private int HeaderHeight => U(BaseHeaderHeight);
    private int ColumnHeaderHeight => U(BaseColumnHeaderHeight);
    private int RailTabHeight => U(BaseRailTabHeight);
    private int RailWidthPx => U(RailWidth);

    private static readonly Color GridLine = Color.FromArgb(72, 78, 86);
    private static readonly Color BoxHeaderBack = Color.FromArgb(108, 114, 122);
    private static readonly Color HeaderActive = Color.FromArgb(36, 78, 132);
    private static readonly Color HeaderUp = Color.FromArgb(122, 52, 52);
    private static readonly Color HeaderDown = Color.FromArgb(40, 96, 66);
    private static readonly Color BidBar = Color.FromArgb(150, 172, 46, 46);
    private static readonly Color AskBar = Color.FromArgb(150, 34, 126, 76);
    private static readonly Color BidText = Color.FromArgb(255, 138, 138);
    private static readonly Color AskText = Color.FromArgb(126, 220, 164);
    private static readonly Color LastRow = Color.FromArgb(82, 88, 96);
    private static readonly Color LastText = Color.FromArgb(255, 232, 120);
    private static readonly Color DimText = Color.FromArgb(188, 192, 198);
    private static readonly Color LimitUpRow = Color.FromArgb(150, 62, 62);
    private static readonly Color LimitDownRow = Color.FromArgb(30, 108, 72);
    private static readonly Color RailBack = Color.FromArgb(50, 54, 60);
    private static readonly Color RailActive = Color.FromArgb(36, 78, 132);
    private static readonly Color BuyHover = Color.FromArgb(58, 200, 64, 64);
    private static readonly Color SellHover = Color.FromArgb(58, 40, 180, 100);

    private readonly List<string> _contracts = [];
    private readonly Dictionary<string, DateTime> _flashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _scrolls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _priceFont = new("新宋体", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _smallFont = new("新宋体", 8f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _volumeFont = new("新宋体", 6.8f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _tinyFont = new("微软雅黑", 6.5f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _headerFont = new("微软雅黑", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _headerPriceFont = new("新宋体", 11f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _columnFont = new("微软雅黑", 6.8f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _railFont = new("微软雅黑", 6.5f, FontStyle.Bold, GraphicsUnit.Point);

    private int _activeColumn;
    private int _hoverColumn = -1;
    private int _hoverRow = -1;
    private bool _hoverBuySide = true;
    private float _ui = 1f;

    public PriceLadder()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.GridBack;
    }

    public Func<string, Quote?>? QuoteProvider { get; set; }

    public Func<string, Instrument?>? InstrumentProvider { get; set; }

    public Func<string, IReadOnlyList<Order>>? OrderProvider { get; set; }

    public Func<string, IReadOnlyList<Position>>? PositionProvider { get; set; }

    /// <summary>合约锁/挂单锁状态，用于在竖框标题上显示锁标记。</summary>
    public Func<string, string?>? LockProvider { get; set; }

    /// <summary>深度：中心价上下各显示多少档（决定目标行数）。</summary>
    public int Depth { get; set; } = 10;

    /// <summary>价格步长，单位为最小变动价位。</summary>
    public int StepTicks { get; set; } = 1;

    /// <summary>滚轮一次滚动的行数。</summary>
    public int WheelRows { get; set; } = 1;

    /// <summary>每个合约竖框的宽度，由主窗体按可用宽度均分。</summary>
    public int BoxWidth { get; set; } = 168;

    /// <summary>是否把本账户挂单价位显示为白格（点价窗的「白格」开关）。</summary>
    public bool ShowWorkingOrders { get; set; } = true;

    /// <summary>是否绘制买卖盘口深度条。</summary>
    public bool ShowDepthBars { get; set; } = true;

    /// <summary>左侧细长列表栏的分组数量。</summary>
    public int GroupCount { get; set; } = 1;

    /// <summary>当前分组（从 1 开始）。</summary>
    public int ActiveGroup { get; set; } = 1;

    public int ActiveColumn => _activeColumn;

    public event EventHandler<LadderCellEventArgs>? CellClicked;

    public event EventHandler<string>? ColumnActivated;

    public event EventHandler<int>? GroupRequested;

    public string? ActiveInstrument =>
        _activeColumn >= 0 && _activeColumn < _contracts.Count ? _contracts[_activeColumn] : null;

    /// <summary>最新价所在行的顶部像素，供主窗体的价格线对齐。</summary>
    public int CenterRowTop =>
        HeaderHeight + ColumnHeaderHeight + (int)Math.Round((VisibleRowCount / 2) * RowHeight);

    public void SetContracts(IEnumerable<string> contracts)
    {
        _contracts.Clear();
        _contracts.AddRange(contracts.Take(MaxColumns));
        _activeColumn = Math.Clamp(_activeColumn, 0, Math.Max(0, _contracts.Count - 1));

        foreach (var id in _scrolls.Keys.Where(k => !_contracts.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _scrolls.Remove(id);
        }

        Invalidate();
    }

    public void SetActiveColumn(int column)
    {
        if (column < 0 || column >= _contracts.Count)
        {
            return;
        }

        _activeColumn = column;
        ColumnActivated?.Invoke(this, _contracts[column]);
        Invalidate();
    }

    public void Flash(string instrumentId, double price)
    {
        _flashes[$"{instrumentId}|{price:0.####}"] = DateTime.UtcNow;
        Invalidate();
    }

    public void ScrollRows(int delta)
    {
        if (_contracts.Count == 0)
        {
            return;
        }

        ScrollBox(_contracts[Math.Clamp(_activeColumn, 0, _contracts.Count - 1)], delta);
    }

    public void ResetScroll()
    {
        _scrolls.Clear();
        Invalidate();
    }

    private int U(int value) => (int)Math.Round(value * _ui);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ui = DeviceDpi / 96f;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _ui = DeviceDpi / 96f;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var column = BoxAt(e.X);
        var row = RowAt(e.Y);
        var buySide = column < 0 || IsBuySide(column, e.X);
        if (column != _hoverColumn || row != _hoverRow || buySide != _hoverBuySide)
        {
            _hoverColumn = column;
            _hoverRow = row;
            _hoverBuySide = buySide;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverColumn = -1;
        _hoverRow = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            // 右键先激活该竖框，随后的 ContextMenuStrip 即针对该合约。
            var target = BoxAt(e.X);
            if (target >= 0 && target < _contracts.Count)
            {
                SetActiveColumn(target);
            }

            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (e.X < RailWidthPx)
        {
            var index = e.Y / Math.Max(1, RailTabHeight);
            var group = RailFirstGroup + index;
            if (index < RailVisibleCount && group >= 1 && group <= Math.Max(1, GroupCount))
            {
                ActiveGroup = group;
                GroupRequested?.Invoke(this, group);
                Invalidate();
            }

            return;
        }

        var column = BoxAt(e.X);
        if (column < 0 || column >= _contracts.Count)
        {
            return;
        }

        SetActiveColumn(column);

        var row = RowAt(e.Y);
        if (row < 0)
        {
            return;
        }

        var contract = _contracts[column];
        var quote = QuoteProvider?.Invoke(contract);
        if (quote is null)
        {
            return;
        }

        var price = PriceAt(contract, quote, row);
        if (price <= 0)
        {
            return;
        }

        var step = StepOf(contract);
        var hasOrder = OrderProvider?.Invoke(contract).Any(o =>
            Math.Abs(o.Price - price) < step / 2) == true;

        // 原版竖框左半边做多、右半边做空。
        var buySide = IsBuySide(column, e.X);

        CellClicked?.Invoke(
            this,
            new LadderCellEventArgs
            {
                InstrumentId = contract,
                Price = price,
                Direction = buySide ? OrderDirection.Buy : OrderDirection.Sell,
                HasWorkingOrder = hasOrder,
                SubColumn = SubColumnAt(column, e.X),
            });
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        var delta = -Math.Sign(e.Delta) * Math.Max(1, WheelRows);
        if (e.X < RailWidthPx)
        {
            var group = Math.Clamp(ActiveGroup - Math.Sign(e.Delta), 1, Math.Max(1, GroupCount));
            if (group != ActiveGroup)
            {
                ActiveGroup = group;
                GroupRequested?.Invoke(this, group);
                Invalidate();
            }

            return;
        }

        var column = BoxAt(e.X);
        if (column < 0 || column >= _contracts.Count)
        {
            ScrollRows(delta);
            return;
        }

        SetActiveColumn(column);
        ScrollBox(_contracts[column], delta);
    }

    private void ScrollBox(string contract, int delta)
    {
        var current = _scrolls.TryGetValue(contract, out var value) ? value : 0;
        _scrolls[contract] = Math.Clamp(current + delta, -VisibleRowCount, VisibleRowCount);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var back = new SolidBrush(Theme.GridBack);
        g.FillRectangle(back, ClientRectangle);

        DrawRail(g);

        if (_contracts.Count == 0)
        {
            TextRenderer.DrawText(
                g,
                "载入中......",
                _headerFont,
                new Rectangle(RailWidthPx + U(6), U(6), Math.Max(1, Width - RailWidthPx - U(12)), U(24)),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        for (var column = 0; column < _contracts.Count; column++)
        {
            DrawBox(g, column, _contracts[column]);
        }

        using var frame = new Pen(Color.FromArgb(96, 102, 110));
        g.DrawLine(frame, RailWidthPx - 1, 0, RailWidthPx - 1, Height);
    }

    /// <summary>最左侧细长列表栏：原版用「分组」标签切换不同的合约列表。</summary>
    private void DrawRail(Graphics g)
    {
        using var rail = new SolidBrush(RailBack);
        g.FillRectangle(rail, 0, 0, RailWidthPx, Height);

        var count = Math.Max(1, GroupCount);
        var visible = Math.Min(count, RailVisibleCount);
        var first = RailFirstGroup;
        for (var i = 0; i < visible; i++)
        {
            var group = first + i;
            var top = i * RailTabHeight;
            if (top >= Height)
            {
                break;
            }

            var active = group == ActiveGroup;
            if (active)
            {
                using var fill = new SolidBrush(RailActive);
                g.FillRectangle(fill, 0, top, RailWidthPx, Math.Min(RailTabHeight, Height - top) - 1);
            }

            TextRenderer.DrawText(
                g,
                group.ToString(),
                _railFont,
                new Rectangle(0, top, RailWidthPx, Math.Min(RailTabHeight, Height - top) - 1),
                active ? Color.White : Color.FromArgb(176, 182, 190),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        using var split = new Pen(Color.FromArgb(88, 94, 102));
        g.DrawLine(split, 0, 0, 0, Height);
    }

    private void DrawBox(Graphics g, int column, string contract)
    {
        var left = BoxLeft(column);
        var width = BoxWidth;
        var quote = QuoteProvider?.Invoke(contract);
        var instrument = InstrumentProvider?.Invoke(contract);
        var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 0.01;
        var isActive = column == _activeColumn;

        DrawBoxHeader(g, left, width, contract, quote, tick, isActive);

        using var line = new Pen(GridLine);
        var columnText = Color.FromArgb(168, 174, 180);

        // 列名行背景提示：左半边红色做多、右半边绿色做空。
        using (var buyTint = new SolidBrush(Color.FromArgb(38, 200, 60, 60)))
        {
            g.FillRectangle(buyTint, left, HeaderHeight, width / 2, ColumnHeaderHeight);
        }

        using (var sellTint = new SolidBrush(Color.FromArgb(38, 40, 170, 100)))
        {
            g.FillRectangle(sellTint, left + width / 2, HeaderHeight, width - width / 2, ColumnHeaderHeight);
        }

        for (var i = 0; i < ColumnWeights.Length; i++)
        {
            var cellLeft = left + SubColumnEdge(i);
            TextRenderer.DrawText(
                g,
                ColumnNames[i],
                _columnFont,
                new Rectangle(cellLeft, HeaderHeight, SubColumnEdge(i + 1) - SubColumnEdge(i), ColumnHeaderHeight),
                columnText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        g.DrawLine(line, left, HeaderHeight, left + width, HeaderHeight);
        g.DrawLine(line, left, HeaderHeight + ColumnHeaderHeight, left + width, HeaderHeight + ColumnHeaderHeight);

        if (quote is null)
        {
            TextRenderer.DrawText(
                g,
                "等待行情",
                _smallFont,
                new Rectangle(left + 1, HeaderHeight + ColumnHeaderHeight, width - 2, 40),
                DimText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            DrawBoxFrame(g, left, width, isActive);
            return;
        }

        var step = StepOf(contract);
        var rowsTop = HeaderHeight + ColumnHeaderHeight;
        var rowHeight = RowHeight;
        var rows = VisibleRowCount;
        var half = rows / 2;
        var offset = _scrolls.TryGetValue(contract, out var scroll) ? scroll : 0;
        var center = quote.LastPrice + offset * step;
        var positions = PositionProvider?.Invoke(contract) ?? [];
        var orders = OrderProvider?.Invoke(contract) ?? [];

        var maxVolume = 1;
        foreach (var level in quote.Bids)
        {
            maxVolume = Math.Max(maxVolume, level.Volume);
        }

        foreach (var level in quote.Asks)
        {
            maxVolume = Math.Max(maxVolume, level.Volume);
        }

        for (var row = 0; row <= rows; row++)
        {
            var y = rowsTop + (int)Math.Round(row * rowHeight);
            var bottom = rowsTop + (int)Math.Round((row + 1) * rowHeight);
            var height = Math.Min(bottom, Height) - y;
            if (height <= 0)
            {
                break;
            }

            var price = RoundPrice(center + (half - row) * step, step);
            var rowRect = new Rectangle(left + 1, y, width - 2, height - 1);
            var bidVolume = VolumeAt(quote.Bids, price, step);
            var askVolume = VolumeAt(quote.Asks, price, step);
            var isLast = Math.Abs(price - quote.LastPrice) < step / 2;
            var isLimitUp = quote.UpperLimitPrice > 0 && price >= quote.UpperLimitPrice;
            var isLimitDown = quote.LowerLimitPrice > 0 && price <= quote.LowerLimitPrice;

            if ((row & 1) == 1)
            {
                using var stripe = new SolidBrush(Color.FromArgb(16, 255, 255, 255));
                g.FillRectangle(stripe, rowRect);
            }

            if (isLimitUp)
            {
                using var up = new SolidBrush(LimitUpRow);
                g.FillRectangle(up, rowRect);
            }
            else if (isLimitDown)
            {
                using var down = new SolidBrush(LimitDownRow);
                g.FillRectangle(down, rowRect);
            }

            if (isLast)
            {
                using var last = new SolidBrush(LastRow);
                g.FillRectangle(last, rowRect);
            }

            if (ShowDepthBars)
            {
                DrawVolumeBar(g, left, rowRect, 0, bidVolume, maxVolume, true);
                DrawVolumeBar(g, left, rowRect, 4, askVolume, maxVolume, false);
            }

            var working = ShowWorkingOrders
                ? orders.FirstOrDefault(o =>
                    (o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled) &&
                    Math.Abs(o.Price - price) < step / 2)
                : null;

            if (working is not null)
            {
                using var white = new SolidBrush(Color.White);
                g.FillRectangle(white, rowRect);
            }

            if (_flashes.TryGetValue($"{contract}|{price:0.####}", out var flashTime))
            {
                var elapsed = DateTime.UtcNow - flashTime;
                if (elapsed < TimeSpan.FromMilliseconds(1200))
                {
                    var alpha = (int)(200 * (1 - elapsed.TotalMilliseconds / 1200));
                    using var flash = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 200), 255, 168, 48));
                    g.FillRectangle(flash, rowRect);
                }
            }

            // 悬停行：左半边红色（做多）、右半边绿色（做空），对应原版点击方向。
            if (row == _hoverRow && column == _hoverColumn)
            {
                var mid = rowRect.X + rowRect.Width / 2;
                using var buy = new SolidBrush(BuyHover);
                using var sell = new SolidBrush(SellHover);
                g.FillRectangle(buy, rowRect.X, rowRect.Y, mid - rowRect.X, rowRect.Height);
                g.FillRectangle(sell, mid, rowRect.Y, rowRect.Right - mid, rowRect.Height);
                using var focus = new Pen(Color.FromArgb(210, 255, 255, 255));
                g.DrawRectangle(focus, rowRect.X, rowRect.Y, rowRect.Width - 1, rowRect.Height - 1);
            }

            var priceText = FormatPrice(price, tick);

            // 买价 / 卖价列：只有该价位确有对应盘口时才显示。
            if (bidVolume > 0 || isLast)
            {
                DrawCellText(g, left, 1, y, height, priceText, _priceFont,
                    working is not null ? Color.Black : isLast ? LastText : BidText,
                    TextFormatFlags.HorizontalCenter);
            }

            if (askVolume > 0 || isLast)
            {
                DrawCellText(g, left, 3, y, height, priceText, _priceFont,
                    working is not null ? Color.Black : isLast ? LastText : AskText,
                    TextFormatFlags.HorizontalCenter);
            }

            // 中间列：最新价高亮；无盘口的价位用暗色显示，便于定位。
            if (isLast || (bidVolume <= 0 && askVolume <= 0))
            {
                DrawCellText(g, left, 2, y, height, priceText,
                    isLast ? _priceFont : _smallFont,
                    working is not null ? Color.Black : isLast ? LastText : DimText,
                    TextFormatFlags.HorizontalCenter);
            }

            if (bidVolume > 0)
            {
                DrawCellText(g, left, 0, y, height, bidVolume.ToString(), _volumeFont,
                    working is not null ? Color.Black : Color.White,
                    TextFormatFlags.HorizontalCenter);
            }

            if (askVolume > 0)
            {
                DrawCellText(g, left, 4, y, height, askVolume.ToString(), _volumeFont,
                    working is not null ? Color.Black : Color.White,
                    TextFormatFlags.HorizontalCenter);
            }

            if (working is not null)
            {
                var label = (working.Direction == OrderDirection.Buy ? "买" : "卖") + working.Volume;
                var labelColumn = working.Direction == OrderDirection.Buy ? 0 : 4;
                DrawCellText(g, left, labelColumn, y, height, label, _volumeFont,
                    working.Direction == OrderDirection.Buy
                        ? Color.FromArgb(160, 30, 30)
                        : Color.FromArgb(20, 90, 50),
                    TextFormatFlags.HorizontalCenter);
            }

            foreach (var position in positions)
            {
                if (position.Volume <= 0 || Math.Abs(position.AvgPrice - price) >= step / 2)
                {
                    continue;
                }

                var longSide = position.Direction == PositionDirection.Long;
                using var marker = new SolidBrush(
                    longSide ? Color.FromArgb(0, 208, 220) : Color.FromArgb(255, 150, 40));
                var markerX = longSide ? rowRect.X : rowRect.Right - 3;
                g.FillRectangle(marker, markerX, rowRect.Y, 3, rowRect.Height);

                TextRenderer.DrawText(
                    g,
                    (longSide ? "多" : "空") + position.Volume,
                    _tinyFont,
                    longSide
                        ? new Rectangle(rowRect.X + 3, rowRect.Y, 26, rowRect.Height)
                        : new Rectangle(rowRect.Right - 29, rowRect.Y, 26, rowRect.Height),
                    marker.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (isLast)
            {
                using var arrow = new SolidBrush(LastText);
                var points = new[]
                {
                    new Point(rowRect.Right - 7, rowRect.Y + rowRect.Height / 2 - 4),
                    new Point(rowRect.Right - 2, rowRect.Y + rowRect.Height / 2),
                    new Point(rowRect.Right - 7, rowRect.Y + rowRect.Height / 2 + 4),
                };
                g.FillPolygon(arrow, points);
            }

            g.DrawLine(line, rowRect.X, rowRect.Bottom, rowRect.Right, rowRect.Bottom);
        }

        // 五列分隔线。
        for (var i = 1; i < ColumnWeights.Length; i++)
        {
            var x = left + SubColumnEdge(i);
            g.DrawLine(line, x, HeaderHeight, x, Height);
        }

        DrawBoxFrame(g, left, width, isActive);
    }

    /// <summary>竖框标题：合约代码、涨跌幅、最新价与成交量，锁定时显示「锁」标。</summary>
    private void DrawBoxHeader(
        Graphics g,
        int left,
        int width,
        string contract,
        Quote? quote,
        double tick,
        bool active)
    {
        var change = quote is null || quote.PreSettlementPrice <= 0
            ? 0
            : (quote.LastPrice - quote.PreSettlementPrice) / quote.PreSettlementPrice;

        var back = active
            ? HeaderActive
            : quote is null || quote.PreSettlementPrice <= 0
                ? BoxHeaderBack
                : change > 0
                    ? HeaderUp
                    : change < 0
                        ? HeaderDown
                        : BoxHeaderBack;

        using (var header = new SolidBrush(back))
        {
            g.FillRectangle(header, left, 0, width, HeaderHeight);
        }

        var lockText = LockProvider?.Invoke(contract);
        var badgeWidth = string.IsNullOrEmpty(lockText) ? 0 : U(18);
        var pad = U(4);
        var textWidth = width - U(8) - badgeWidth;
        var lineHeight = U(16);

        TextRenderer.DrawText(
            g,
            ShortId(contract),
            _headerFont,
            new Rectangle(left + pad, U(1), textWidth, lineHeight),
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (quote is not null)
        {
            TextRenderer.DrawText(
                g,
                change.ToString("+0.00%;-0.00%;0.00%"),
                _tinyFont,
                new Rectangle(left + pad, U(1), textWidth, lineHeight),
                change > 0 ? Color.FromArgb(255, 168, 168) : change < 0
                    ? Color.FromArgb(150, 255, 190)
                    : Color.Gainsboro,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                g,
                FormatPrice(quote.LastPrice, tick),
                _headerPriceFont,
                new Rectangle(left + pad, U(15), textWidth, U(20)),
                active ? LastText : Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                g,
                FormatVolume(quote.Volume),
                _tinyFont,
                new Rectangle(left + pad, U(15), textWidth, U(20)),
                Color.FromArgb(216, 220, 226),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (!string.IsNullOrEmpty(lockText))
        {
            var badge = new Rectangle(left + width - badgeWidth, U(2), U(14), U(13));
            using var fill = new SolidBrush(Color.FromArgb(226, 168, 32));
            g.FillRectangle(fill, badge);
            TextRenderer.DrawText(
                g,
                lockText,
                _tinyFont,
                badge,
                Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawBoxFrame(Graphics g, int left, int width, bool active)
    {
        if (active)
        {
            using var border = new Pen(Color.FromArgb(64, 156, 235), 2);
            g.DrawRectangle(border, new Rectangle(left + 1, 1, width - 2, Height - 3));
        }
        else
        {
            using var border = new Pen(GridLine);
            g.DrawRectangle(border, new Rectangle(left, 0, width - 1, Height - 1));
        }
    }

    private void DrawVolumeBar(
        Graphics g,
        int left,
        Rectangle row,
        int subColumn,
        int volume,
        int maxVolume,
        bool bid)
    {
        if (volume <= 0)
        {
            return;
        }

        var cellLeft = left + SubColumnEdge(subColumn);
        var cellWidth = SubColumnEdge(subColumn + 1) - SubColumnEdge(subColumn);
        var ratio = Math.Clamp(volume / (double)maxVolume, 0.12, 1);
        var barWidth = Math.Max(2, (int)Math.Round(cellWidth * ratio));
        using var brush = new SolidBrush(bid ? BidBar : AskBar);
        var rect = bid
            ? new Rectangle(cellLeft, row.Y, barWidth, row.Height)
            : new Rectangle(cellLeft + cellWidth - barWidth, row.Y, barWidth, row.Height);
        g.FillRectangle(brush, rect);
    }

    private void DrawCellText(
        Graphics g,
        int left,
        int subColumn,
        int y,
        int height,
        string text,
        Font font,
        Color color,
        TextFormatFlags flags)
    {
        var x = left + SubColumnEdge(subColumn);
        var width = SubColumnEdge(subColumn + 1) - SubColumnEdge(subColumn);
        TextRenderer.DrawText(
            g,
            text,
            font,
            new Rectangle(x, y, width, height),
            color,
            flags | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static int VolumeAt(IReadOnlyList<DepthLevel> levels, double price, double step)
    {
        foreach (var level in levels)
        {
            if (Math.Abs(level.Price - price) < step / 2)
            {
                return level.Volume;
            }
        }

        return 0;
    }

    /// <summary>原软件 config.ini 的 PriceListRatios=10,25,30,25,10。</summary>
    private static readonly int[] ColumnWeights = [10, 25, 30, 25, 10];

    private static readonly string[] ColumnNames = ["买量", "买价", "最新", "卖价", "卖量"];

    private int SubColumnEdge(int index)
    {
        var total = 0;
        foreach (var weight in ColumnWeights)
        {
            total += weight;
        }

        var acc = 0;
        for (var i = 0; i < index && i < ColumnWeights.Length; i++)
        {
            acc += ColumnWeights[i];
        }

        return (int)Math.Round(BoxWidth * (double)acc / total);
    }

    private int BoxLeft(int column) => RailWidthPx + column * BoxWidth;

    /// <summary>行高在 16~26 像素之间自适应，保证高位窗口下仍显示足够多的价位。</summary>
    private float RowHeight =>
        Math.Clamp((Height - HeaderHeight - ColumnHeaderHeight) / (float)(Depth * 2 + 1), U(16), U(26));

    private int VisibleRowCount =>
        Math.Max(4, (int)Math.Floor((Height - HeaderHeight - ColumnHeaderHeight) / RowHeight));

    /// <summary>列表栏一次最多显示的分组标签数，分组很多时只显示当前附近的窗口。</summary>
    private int RailVisibleCount => Math.Max(1, Height / Math.Max(1, RailTabHeight));

    private int RailFirstGroup
    {
        get
        {
            var count = Math.Max(1, GroupCount);
            var visible = Math.Min(count, RailVisibleCount);
            return Math.Clamp(ActiveGroup - visible / 2, 1, Math.Max(1, count - visible + 1));
        }
    }

    private int BoxAt(int x) => x < RailWidthPx ? -1 : (x - RailWidthPx) / Math.Max(1, BoxWidth);

    private int SubColumnAt(int column, int x)
    {
        var local = x - BoxLeft(column);
        for (var i = 1; i < ColumnWeights.Length; i++)
        {
            if (local < SubColumnEdge(i))
            {
                return i - 1;
            }
        }

        return ColumnWeights.Length - 1;
    }

    /// <summary>竖框左半边做多、右半边做空。</summary>
    private bool IsBuySide(int column, int x) => x - BoxLeft(column) < BoxWidth / 2;

    private int RowAt(int y)
    {
        var top = HeaderHeight + ColumnHeaderHeight;
        return y < top ? -1 : (int)Math.Floor((y - top) / RowHeight);
    }

    private double StepOf(string contract)
    {
        var instrument = InstrumentProvider?.Invoke(contract);
        var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 0.01;
        return Math.Max(tick, 0.0001) * StepTicks;
    }

    private double PriceAt(string contract, Quote quote, int row)
    {
        var step = StepOf(contract);
        var offset = _scrolls.TryGetValue(contract, out var scroll) ? scroll : 0;
        var center = quote.LastPrice + offset * step;
        var half = VisibleRowCount / 2;
        return RoundPrice(center + (half - row) * step, step);
    }

    private static double RoundPrice(double price, double step) =>
        Math.Round(Math.Round(price / step) * step, 6);

    private static string FormatPrice(double price, double tick)
    {
        if (tick >= 1)
        {
            return price.ToString("0");
        }

        if (tick >= 0.1)
        {
            return price.ToString("0.0");
        }

        if (tick >= 0.01)
        {
            return price.ToString("0.00");
        }

        return price.ToString("0.000");
    }

    private static string FormatVolume(int volume) => volume switch
    {
        >= 1_000_000 => (volume / 10_000d).ToString("0") + "万",
        >= 10_000 => (volume / 10_000d).ToString("0.0") + "万",
        _ => volume.ToString(),
    };

    private static string ShortId(string contract)
    {
        if (contract.StartsWith("SPD:", StringComparison.OrdinalIgnoreCase))
        {
            return "价差";
        }

        return contract.Length <= 7 ? contract : contract[..7];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _priceFont.Dispose();
            _smallFont.Dispose();
            _volumeFont.Dispose();
            _tinyFont.Dispose();
            _headerFont.Dispose();
            _headerPriceFont.Dispose();
            _columnFont.Dispose();
            _railFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
