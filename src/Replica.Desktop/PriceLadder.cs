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
}

/// <summary>
/// 原软件 TYYWin.SGPriceList 的复刻：5 列行情梯。
/// 每列一个合约，按最小变动价位纵向排列，深度档位用买卖量条显示，
/// 白格代表本账户挂单，左侧竖条代表持仓均价。
/// </summary>
internal sealed class PriceLadder : Control
{
    private const int HeaderHeight = 22;
    public const int MaxColumns = 5;

    private static readonly Color GridLine = Color.FromArgb(150, 154, 160);
    private static readonly Color HeaderBack = Color.FromArgb(108, 114, 122);
    private static readonly Color HeaderActive = Color.FromArgb(56, 100, 152);
    private static readonly Color HeaderUp = Color.FromArgb(122, 52, 52);
    private static readonly Color HeaderDown = Color.FromArgb(40, 96, 66);
    private static readonly Color BidBar = Color.FromArgb(132, 24, 116, 74);
    private static readonly Color AskBar = Color.FromArgb(132, 168, 62, 48);
    private static readonly Color LastRow = Color.FromArgb(82, 88, 96);
    private static readonly Color LimitUpRow = Color.FromArgb(150, 62, 62);
    private static readonly Color LimitDownRow = Color.FromArgb(30, 108, 72);

    private readonly List<string> _contracts = [];
    private readonly Dictionary<string, DateTime> _flashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _priceFont = new("新宋体", 13f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _smallFont = new("新宋体", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _tinyFont = new("微软雅黑", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _headerFont = new("微软雅黑", 8.5f, FontStyle.Bold, GraphicsUnit.Point);

    private int _activeColumn;
    private int _scrollOffset;
    private int _hoverColumn = -1;
    private int _hoverRow = -1;

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

    /// <summary>深度：中心价上下各显示多少档（决定目标行数）。</summary>
    public int Depth { get; set; } = 10;

    /// <summary>价格步长，单位为最小变动价位。</summary>
    public int StepTicks { get; set; } = 1;

    /// <summary>滚轮一次滚动的行数。</summary>
    public int WheelRows { get; set; } = 1;

    /// <summary>是否把本账户挂单价位显示为白格（点价窗的「白格」开关）。</summary>
    public bool ShowWorkingOrders { get; set; } = true;

    /// <summary>是否绘制买卖盘口深度条。</summary>
    public bool ShowDepthBars { get; set; } = true;

    public int ActiveColumn => _activeColumn;

    public event EventHandler<LadderCellEventArgs>? CellClicked;

    public event EventHandler<string>? ColumnActivated;

    public string? ActiveInstrument =>
        _activeColumn >= 0 && _activeColumn < _contracts.Count ? _contracts[_activeColumn] : null;

    /// <summary>最新价所在行的顶部像素，供主窗体的价格线对齐。</summary>
    public int CenterRowTop => HeaderHeight + (int)Math.Round((VisibleRowCount / 2) * RowHeight);

    public void SetContracts(IEnumerable<string> contracts)
    {
        _contracts.Clear();
        _contracts.AddRange(contracts.Take(MaxColumns));
        _activeColumn = Math.Clamp(_activeColumn, 0, Math.Max(0, _contracts.Count - 1));
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
        _scrollOffset = Math.Clamp(_scrollOffset + delta, -VisibleRowCount, VisibleRowCount);
        Invalidate();
    }

    public void ResetScroll()
    {
        _scrollOffset = 0;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var column = ColumnAt(e.X);
        var row = RowAt(e.Y);
        if (column != _hoverColumn || row != _hoverRow)
        {
            _hoverColumn = column;
            _hoverRow = row;
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
        if (_contracts.Count == 0)
        {
            return;
        }

        var column = ColumnAt(e.X);
        if (column < 0 || column >= _contracts.Count)
        {
            return;
        }

        SetActiveColumn(column);

        if (e.Y < HeaderHeight)
        {
            return;
        }

        var contract = _contracts[column];
        var quote = QuoteProvider?.Invoke(contract);
        if (quote is null)
        {
            return;
        }

        var row = RowAt(e.Y);
        if (row < 0)
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

        var direction = e.Button == MouseButtons.Right ? OrderDirection.Sell : OrderDirection.Buy;
        CellClicked?.Invoke(
            this,
            new LadderCellEventArgs
            {
                InstrumentId = contract,
                Price = price,
                Direction = direction,
                HasWorkingOrder = hasOrder,
            });
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollRows(-Math.Sign(e.Delta) * Math.Max(1, WheelRows));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var back = new SolidBrush(Theme.GridBack);
        g.FillRectangle(back, ClientRectangle);

        if (_contracts.Count == 0)
        {
            TextRenderer.DrawText(
                g,
                "载入中......",
                _headerFont,
                new Rectangle(6, 6, Width - 12, 24),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        var rowHeight = RowHeight;
        var rows = VisibleRowCount;
        var half = rows / 2;

        using var line = new Pen(GridLine);
        using var headerText = new SolidBrush(Color.Gainsboro);

        for (var column = 0; column < MaxColumns; column++)
        {
            var left = ColumnEdge(column);
            var right = ColumnEdge(column + 1);
            var width = Math.Max(1, right - left);
            var isActive = column == _activeColumn;
            var contract = column < _contracts.Count ? _contracts[column] : null;
            var quote = contract is null ? null : QuoteProvider?.Invoke(contract);

            var headerColor = isActive
                ? HeaderActive
                : quote is null || quote.PreSettlementPrice <= 0
                    ? HeaderBack
                    : quote.LastPrice > quote.PreSettlementPrice
                        ? HeaderUp
                        : quote.LastPrice < quote.PreSettlementPrice
                            ? HeaderDown
                            : HeaderBack;

            using (var header = new SolidBrush(headerColor))
            {
                g.FillRectangle(header, left, 0, width, HeaderHeight);
            }

            if (contract is not null)
            {
                TextRenderer.DrawText(
                    g,
                    ShortId(contract),
                    _headerFont,
                    new Rectangle(left, 0, width, HeaderHeight),
                    isActive ? Color.White : headerText.Color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            if (quote is null)
            {
                g.DrawLine(line, left, 0, left, Height);
                continue;
            }

            var instrument = InstrumentProvider?.Invoke(contract!);
            var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 0.01;
            var step = StepOf(contract!);
            var center = quote.LastPrice + _scrollOffset * step;
            var positions = PositionProvider?.Invoke(contract!) ?? [];
            var orders = OrderProvider?.Invoke(contract!) ?? [];

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
                var y = HeaderHeight + (int)Math.Round(row * rowHeight);
                var bottom = HeaderHeight + (int)Math.Round((row + 1) * rowHeight);
                var height = Math.Min(bottom, Height) - y;
                if (height <= 0)
                {
                    break;
                }

                var price = RoundPrice(center + (half - row) * step, step);
                var cell = new Rectangle(left + 1, y, width - 2, height - 1);

                if ((row & 1) == 1)
                {
                    using var stripe = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
                    g.FillRectangle(stripe, cell);
                }

                var isLast = Math.Abs(price - quote.LastPrice) < step / 2;
                var isLimitUp = quote.UpperLimitPrice > 0 && price >= quote.UpperLimitPrice;
                var isLimitDown = quote.LowerLimitPrice > 0 && price <= quote.LowerLimitPrice;

                if (isLimitUp)
                {
                    using var up = new SolidBrush(LimitUpRow);
                    g.FillRectangle(up, cell);
                }
                else if (isLimitDown)
                {
                    using var down = new SolidBrush(LimitDownRow);
                    g.FillRectangle(down, cell);
                }

                if (ShowDepthBars && !isLast)
                {
                    DrawDepthBar(g, cell, quote.Bids, price, step, maxVolume, true);
                    DrawDepthBar(g, cell, quote.Asks, price, step, maxVolume, false);
                }

                if (isLast)
                {
                    using var last = new SolidBrush(LastRow);
                    g.FillRectangle(last, cell);
                }

                var working = ShowWorkingOrders
                    ? orders.FirstOrDefault(o =>
                        (o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled) &&
                        Math.Abs(o.Price - price) < step / 2)
                    : null;

                if (working is not null)
                {
                    using var white = new SolidBrush(Color.White);
                    g.FillRectangle(white, cell);
                }

                if (_flashes.TryGetValue($"{contract}|{price:0.####}", out var flashTime))
                {
                    var elapsed = DateTime.UtcNow - flashTime;
                    if (elapsed < TimeSpan.FromMilliseconds(1200))
                    {
                        var alpha = (int)(200 * (1 - elapsed.TotalMilliseconds / 1200));
                        using var flash = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 200), 255, 168, 48));
                        g.FillRectangle(flash, cell);
                    }
                }

                foreach (var position in positions)
                {
                    if (position.Volume <= 0 || Math.Abs(position.AvgPrice - price) >= step / 2)
                    {
                        continue;
                    }

                    using var marker = new SolidBrush(
                        position.Direction == PositionDirection.Long
                            ? Color.FromArgb(0, 208, 220)
                            : Color.FromArgb(255, 150, 40));
                    g.FillRectangle(marker, cell.X, cell.Y, 3, cell.Height);

                    TextRenderer.DrawText(
                        g,
                        (position.Direction == PositionDirection.Long ? "多" : "空") + position.Volume,
                        _tinyFont,
                        new Rectangle(cell.X + 3, cell.Y, cell.Width - 4, cell.Height),
                        marker.Color,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                if (row == _hoverRow && column == _hoverColumn)
                {
                    using var hover = new SolidBrush(Color.FromArgb(38, 255, 255, 255));
                    g.FillRectangle(hover, cell);
                }

                var text = FormatPrice(price, tick);
                var font = text.Length >= 6 || width < 54 ? _smallFont : _priceFont;
                var textColor = working is not null
                    ? Color.Black
                    : isLast
                        ? Color.FromArgb(255, 232, 120)
                        : Color.White;

                var textRect = working is not null
                    ? new Rectangle(cell.X + 8, cell.Y, cell.Width - 10, cell.Height)
                    : cell;

                TextRenderer.DrawText(
                    g,
                    text,
                    font,
                    textRect,
                    working is not null ? Color.Black : textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);

                if (working is not null)
                {
                    TextRenderer.DrawText(
                        g,
                        (working.Direction == OrderDirection.Buy ? "买" : "卖") + working.Volume,
                        _tinyFont,
                        new Rectangle(cell.X + 1, cell.Y, cell.Width - 2, cell.Height),
                        working.Direction == OrderDirection.Buy
                            ? Color.FromArgb(160, 30, 30)
                            : Color.FromArgb(20, 90, 50),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                if (isLast)
                {
                    using var arrow = new SolidBrush(Color.FromArgb(255, 232, 120));
                    var points = new[]
                    {
                        new Point(cell.Right - 7, cell.Y + cell.Height / 2 - 4),
                        new Point(cell.Right - 2, cell.Y + cell.Height / 2),
                        new Point(cell.Right - 7, cell.Y + cell.Height / 2 + 4),
                    };
                    g.FillPolygon(arrow, points);
                }

                g.DrawLine(line, cell.X, cell.Bottom, cell.Right, cell.Bottom);
            }

            if (isActive)
            {
                using var tint = new SolidBrush(Color.FromArgb(16, 255, 255, 255));
                g.FillRectangle(tint, left + 1, HeaderHeight, width - 2, Height - HeaderHeight);
            }

            g.DrawLine(line, left, 0, left, Height);
        }

        g.DrawLine(line, 0, HeaderHeight, Width, HeaderHeight);
        g.DrawLine(line, Width - 1, 0, Width - 1, Height);

        if (_activeColumn < _contracts.Count)
        {
            using var active = new Pen(Color.FromArgb(64, 156, 235), 2);
            g.DrawRectangle(
                active,
                new Rectangle(
                    ColumnEdge(_activeColumn),
                    1,
                    Math.Max(1, ColumnEdge(_activeColumn + 1) - ColumnEdge(_activeColumn) - 1),
                    Height - 3));
        }
    }

    private static void DrawDepthBar(
        Graphics g,
        Rectangle cell,
        IReadOnlyList<DepthLevel> levels,
        double price,
        double step,
        int maxVolume,
        bool bid)
    {
        var volume = 0;
        foreach (var level in levels)
        {
            if (Math.Abs(level.Price - price) < step / 2)
            {
                volume = level.Volume;
                break;
            }
        }

        if (volume <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(volume / (double)maxVolume, 0.08, 1);
        var barWidth = Math.Max(2, (int)(cell.Width * 0.46 * ratio));
        using var brush = new SolidBrush(bid ? BidBar : AskBar);
        var rect = bid
            ? new Rectangle(cell.X, cell.Y, barWidth, cell.Height)
            : new Rectangle(cell.Right - barWidth, cell.Y, barWidth, cell.Height);
        g.FillRectangle(brush, rect);
    }

    /// <summary>原软件 config.ini 的 PriceListRatios=10,25,30,25,10：中间列最宽。</summary>
    private static readonly int[] ColumnWeights = [10, 25, 30, 25, 10];

    private int ColumnEdge(int index)
    {
        var total = 0;
        for (var i = 0; i < ColumnWeights.Length; i++)
        {
            total += ColumnWeights[i];
        }

        var acc = 0;
        for (var i = 0; i < index && i < ColumnWeights.Length; i++)
        {
            acc += ColumnWeights[i];
        }

        return (int)Math.Round(Width * (double)acc / total);
    }

    /// <summary>行高在 20~26 像素之间自适应，保证高位窗口下仍显示足够多的价位。</summary>
    private float RowHeight =>
        Math.Clamp((Height - HeaderHeight) / (float)(Depth * 2 + 1), 20f, 26f);

    private int VisibleRowCount =>
        Math.Max(4, (int)Math.Floor((Height - HeaderHeight) / RowHeight));

    private int ColumnAt(int x)
    {
        for (var column = 1; column <= MaxColumns; column++)
        {
            if (x < ColumnEdge(column))
            {
                return column - 1;
            }
        }

        return MaxColumns - 1;
    }

    private int RowAt(int y) => y < HeaderHeight ? -1 : (int)Math.Floor((y - HeaderHeight) / RowHeight);

    private double StepOf(string contract)
    {
        var instrument = InstrumentProvider?.Invoke(contract);
        var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 0.01;
        return Math.Max(tick, 0.0001) * StepTicks;
    }

    private double PriceAt(string contract, Quote quote, int row)
    {
        var step = StepOf(contract);
        var center = quote.LastPrice + _scrollOffset * step;
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
            _tinyFont.Dispose();
            _headerFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
