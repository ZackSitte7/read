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
/// 原软件 TYYWin.SGPriceList 的复刻：5 列固定宽度的行情梯，
/// 每列一个合约，绿色/红色之外保持原版的灰底白字，白格表示本账户挂单。
/// </summary>
internal sealed class PriceLadder : Control
{
    private const int HeaderHeight = 20;
    public const int MaxColumns = 5;

    private readonly List<string> _contracts = [];
    private readonly Dictionary<string, DateTime> _flashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _priceFont;
    private readonly Font _smallFont;
    private readonly Font _headerFont;

    private int _activeColumn;
    private int _scrollOffset;

    public PriceLadder()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.GridBack;
        _priceFont = new Font("新宋体", 13f, FontStyle.Bold, GraphicsUnit.Point);
        _smallFont = new Font("新宋体", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        _headerFont = new Font("微软雅黑", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
    }

    public Func<string, Quote?>? QuoteProvider { get; set; }

    public Func<string, Instrument?>? InstrumentProvider { get; set; }

    public Func<string, IReadOnlyList<Order>>? OrderProvider { get; set; }

    public Func<string, IReadOnlyList<Position>>? PositionProvider { get; set; }

    /// <summary>深度：中心价上下各显示多少档。</summary>
    public int Depth { get; set; } = 10;

    /// <summary>价格步长，单位为最小变动价位。</summary>
    public int StepTicks { get; set; } = 1;

    /// <summary>滚轮一次滚动的行数。</summary>
    public int WheelRows { get; set; } = 1;

    /// <summary>是否把本账户挂单价位显示为白格（点价窗的「白格」开关）。</summary>
    public bool ShowWorkingOrders { get; set; } = true;

    public int ActiveColumn => _activeColumn;

    public event EventHandler<LadderCellEventArgs>? CellClicked;

    public event EventHandler<string>? ColumnActivated;

    public string? ActiveInstrument =>
        _activeColumn >= 0 && _activeColumn < _contracts.Count ? _contracts[_activeColumn] : null;

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
        _scrollOffset = Math.Clamp(_scrollOffset + delta, -Depth * 4, Depth * 4);
        Invalidate();
    }

    public void ResetScroll()
    {
        _scrollOffset = 0;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_contracts.Count == 0)
        {
            return;
        }

        var column = Math.Clamp((int)(e.X / Math.Max(1f, ColumnWidth)), 0, _contracts.Count - 1);
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

        var rowHeight = RowHeight;
        var rows = Depth * 2 + 1;
        var row = (int)Math.Floor((e.Y - HeaderHeight) / rowHeight);
        if (row < 0 || row >= rows)
        {
            return;
        }

        var tick = InstrumentProvider?.Invoke(contract)?.PriceTick ?? 0.01;
        var step = Math.Max(tick, 0.0001) * StepTicks;
        var centerPrice = quote.LastPrice + _scrollOffset * step;
        var price = centerPrice + (Depth - row) * step;
        price = Math.Round(price / step) * step;
        price = Math.Round(price, 4);

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
            using var empty = new SolidBrush(Color.White);
            g.DrawString("载入中......", _headerFont, empty, new PointF(4, 4));
            return;
        }

        var columnWidth = ColumnWidth;
        var rowHeight = RowHeight;
        var rows = Depth * 2 + 1;

        using var line = new Pen(Color.FromArgb(140, 140, 140));
        using var headerBack = new SolidBrush(Color.FromArgb(124, 132, 144));
        using var headerActive = new SolidBrush(Color.FromArgb(138, 148, 162));
        using var headerText = new SolidBrush(Color.Gainsboro);

        for (var column = 0; column < MaxColumns; column++)
        {
            var x = column * columnWidth;
            var isActive = column == _activeColumn;
            var contract = column < _contracts.Count ? _contracts[column] : null;

            g.FillRectangle(
                isActive ? headerActive : headerBack,
                x,
                0,
                columnWidth,
                HeaderHeight);

            var header = contract is null ? string.Empty : ShortId(contract);
            var headerSize = g.MeasureString(header, _headerFont);
            g.DrawString(
                header,
                _headerFont,
                isActive ? Brushes.White : headerText,
                x + (columnWidth - headerSize.Width) / 2,
                2);

            if (contract is null)
            {
                g.DrawLine(line, x, 0, x, Height);
                continue;
            }

            var quote = QuoteProvider?.Invoke(contract);
            if (quote is null)
            {
                g.DrawLine(line, x, 0, x, Height);
                continue;
            }

            var instrument = InstrumentProvider?.Invoke(contract);
            var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 0.01;
            var step = Math.Max(tick, 0.0001) * StepTicks;
            var center = quote.LastPrice + _scrollOffset * step;
            var positions = PositionProvider?.Invoke(contract) ?? [];
            var orders = OrderProvider?.Invoke(contract) ?? [];

            for (var row = 0; row < rows; row++)
            {
                var y = HeaderHeight + row * rowHeight;
                var price = center + (Depth - row) * step;
                price = Math.Round(price / step) * step;

                var cellRect = new Rectangle(
                    (int)Math.Round(x) + 1,
                    (int)Math.Round(y) + 1,
                    (int)Math.Round(columnWidth) - 2,
                    (int)Math.Round(rowHeight) - 2);

                var working = ShowWorkingOrders
                    ? orders.FirstOrDefault(o =>
                        (o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled) &&
                        Math.Abs(o.Price - price) < step / 2)
                    : null;

                using var workingBrush = working is null ? null : new SolidBrush(Color.White);
                if (workingBrush is not null)
                {
                    g.FillRectangle(workingBrush, cellRect);
                }

                var flashKey = $"{contract}|{price:0.####}";
                if (_flashes.TryGetValue(flashKey, out var flashTime) &&
                    DateTime.UtcNow - flashTime < TimeSpan.FromMilliseconds(1500))
                {
                    using var flash = new SolidBrush(Color.FromArgb(190, 255, 170, 60));
                    g.FillRectangle(flash, cellRect);
                }

                if (Math.Abs(price - quote.LastPrice) < step / 2)
                {
                    using var lastBrush = new SolidBrush(Color.FromArgb(78, 82, 88));
                    g.FillRectangle(lastBrush, cellRect);
                }
                else if (quote.UpperLimitPrice > 0 && price >= quote.UpperLimitPrice)
                {
                    using var up = new SolidBrush(Color.FromArgb(176, 74, 74));
                    g.FillRectangle(up, cellRect);
                }
                else if (quote.LowerLimitPrice > 0 && price <= quote.LowerLimitPrice)
                {
                    using var down = new SolidBrush(Color.FromArgb(54, 132, 88));
                    g.FillRectangle(down, cellRect);
                }

                foreach (var position in positions)
                {
                    if (position.Volume <= 0 || Math.Abs(position.AvgPrice - price) >= step / 2)
                    {
                        continue;
                    }

                    using var marker = new SolidBrush(
                        position.Direction == PositionDirection.Long
                            ? Color.FromArgb(0, 210, 220)
                            : Color.FromArgb(255, 150, 40));
                    g.FillRectangle(marker, cellRect.X, cellRect.Y, 3, cellRect.Height);
                }

                var text = FormatPrice(price, tick);
                var font = TextWidth(g, text) > cellRect.Width - 4 ? _smallFont : _priceFont;
                var textColor = working is not null
                    ? Color.Black
                    : Math.Abs(price - quote.LastPrice) < step / 2
                        ? Color.FromArgb(255, 232, 120)
                        : Color.White;

                using var textBrush = new SolidBrush(textColor);
                var size = g.MeasureString(text, font);
                g.DrawString(
                    text,
                    font,
                    textBrush,
                    cellRect.X + (cellRect.Width - size.Width) / 2,
                    cellRect.Y + (cellRect.Height - size.Height) / 2);

                if (working is not null)
                {
                    var caption = working.Direction == OrderDirection.Buy ? "买" : "卖";
                    g.DrawString(caption, _smallFont, Brushes.Black, cellRect.X + 1, cellRect.Y - 1);
                }

                if (row % 2 == 1)
                {
                    g.DrawLine(line, x, (int)y, x + columnWidth, (int)y);
                }
            }

            g.DrawLine(line, x, 0, x, Height);
        }

        g.DrawLine(line, 0, HeaderHeight, Width, HeaderHeight);
        g.DrawLine(line, Width - 1, 0, Width - 1, Height);

        if (_activeColumn < _contracts.Count)
        {
            using var active = new Pen(Color.FromArgb(30, 90, 160), 2);
            g.DrawRectangle(
                active,
                new Rectangle(
                    (int)Math.Round(_activeColumn * columnWidth),
                    1,
                    (int)Math.Round(columnWidth) - 1,
                    Height - 3));
        }
    }

    private float ColumnWidth => Math.Max(1f, Width / (float)MaxColumns);

    private float RowHeight => Math.Max(14f, (Height - HeaderHeight) / (float)(Depth * 2 + 1));

    private float TextWidth(Graphics g, string text) => g.MeasureString(text, _priceFont).Width;

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

        return price.ToString("0.00");
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
            _headerFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
