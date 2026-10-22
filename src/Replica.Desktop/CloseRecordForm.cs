using System.Drawing.Drawing2D;
using Replica.Core.Models;

namespace Replica.Desktop;

/// <summary>
/// 原软件 TCloseRecordWin 的复刻：1067×894、clGainsboro 背景，
/// 内部是一整块 SGCloseRecord（TStringGrid，1000 行、行高 30、新宋体 -26 粗体、
/// goRowSelect、无滚动条、滚轮翻页）。列由 OnDrawCell 在运行时绘制。
/// </summary>
internal sealed class CloseRecordForm : Form
{
    private readonly ReplicaContext _ctx;
    private readonly RecordGrid _grid;
    private readonly Label _stats = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    internal CloseRecordForm(ReplicaContext context)
    {
        _ctx = context;

        Text = "平仓";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1067), Theme.S(924));
        MinimumSize = new Size(Theme.S(600), Theme.S(300));
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Gainsboro;
        Font = Theme.F(18, false, "Segoe UI");
        DoubleBuffered = true;

        _grid = new RecordGrid
        {
            Name = "SGCloseRecord",
            Dock = DockStyle.Fill,
            RowCount = 1000,
        };

        _grid.RecordsProvider = LoadRecords;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(30),
            BackColor = Color.Gainsboro,
        };

        _stats.Dock = DockStyle.Fill;
        _stats.TextAlign = ContentAlignment.MiddleLeft;
        _stats.Font = Theme.F(20, true, "新宋体");
        _stats.ForeColor = Color.DimGray;
        _stats.Padding = new Padding(Theme.S(8), 0, 0, 0);
        _stats.Text = "暂无平仓记录";
        footer.Controls.Add(_stats);

        Controls.Add(_grid);
        Controls.Add(footer);

        _ctx.Log.Appended += OnLogAppended;
        FormClosed += (_, _) =>
        {
            _ctx.Log.Appended -= OnLogAppended;
            _timer.Dispose();
        };

        UpdateStats();
        _timer.Tick += (_, _) => UpdateStats();
        _timer.Start();
    }

    private IReadOnlyList<CloseRecord> LoadRecords() =>
        _ctx.MockTrade?.GetCloseRecords() ?? [];

    private void UpdateStats()
    {
        var records = LoadRecords();
        if (records.Count == 0)
        {
            _stats.Text = "暂无平仓记录";
            _stats.ForeColor = Color.DimGray;
            return;
        }

        var total = records.Sum(r => r.Pnl);
        var wins = records.Count(r => r.Pnl > 0);
        var rate = wins * 100.0 / records.Count;
        var volume = records.Sum(r => r.Volume);
        _stats.Text = $"共 {records.Count} 笔    手数 {volume}    " +
                      $"总盈亏 {total:+0.00;-0.00;0.00}    胜率 {rate:0.0}%    " +
                      $"盈利 {wins} 笔 / 亏损 {records.Count - wins} 笔";
        _stats.ForeColor = total >= 0 ? Color.Firebrick : Color.SeaGreen;
    }

    private void OnLogAppended(object? sender, string line)
    {
        if (!IsDisposed && line.Contains("成交", StringComparison.Ordinal))
        {
            _grid.Invalidate();
            UpdateStats();
        }
    }
}

/// <summary>
/// TStringGrid 的最小复刻：自绘表头 + 行、goRowSelect 单选整行、
/// ssNone 无滚动条、滚轮滚动。数据来自 MockTrade 的平仓记录。
/// </summary>
internal sealed class RecordGrid : Control
{
    private static readonly string[] Headers =
        ["时间", "合约", "方向", "开仓价", "平仓价", "手数", "盈亏"];

    private readonly Font _font = new("新宋体", 13f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _headerFont = new("新宋体", 10.5f, FontStyle.Bold, GraphicsUnit.Point);

    private int _topRow;
    private int _selectedRow = -1;

    public RecordGrid()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = SystemColors.Control;
        ForeColor = Color.Black;
    }

    public Func<IReadOnlyList<CloseRecord>>? RecordsProvider { get; set; }

    public int RowCount { get; set; } = 1000;

    private int RowHeight => Theme.S(30);

    private int HeaderHeight => Theme.S(30);

    private int VisibleRows => Math.Max(1, (Height - HeaderHeight) / RowHeight);

    private int ColumnWidth => Math.Max(40, Width / Headers.Length);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;

        using var back = new SolidBrush(BackColor);
        g.FillRectangle(back, ClientRectangle);

        var rows = RecordsProvider?.Invoke() ?? [];
        var maxTop = Math.Max(0, Math.Min(rows.Count, RowCount) - VisibleRows);
        if (_topRow > maxTop)
        {
            _topRow = maxTop;
            _selectedRow = -1;
        }

        using var gridPen = new Pen(Color.FromArgb(160, 160, 160));
        using var headerBrush = new SolidBrush(Color.FromArgb(214, 214, 214));
        using var selectedBrush = new SolidBrush(Color.FromArgb(49, 106, 197));
        using var altBrush = new SolidBrush(Color.FromArgb(235, 235, 235));
        using var whiteBrush = new SolidBrush(Color.Black);
        using var selectedText = new SolidBrush(Color.White);

        g.FillRectangle(headerBrush, 0, 0, Width, HeaderHeight);
        for (var c = 0; c < Headers.Length; c++)
        {
            var x = c * ColumnWidth;
            var rect = new Rectangle(x + 2, 0, ColumnWidth - 4, HeaderHeight);
            g.DrawString(Headers[c], _headerFont, whiteBrush, rect,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            g.DrawLine(gridPen, x, 0, x, Height);
        }

        g.DrawLine(gridPen, 0, HeaderHeight, Width, HeaderHeight);

        var bottom = Height;
        for (var i = 0; i < VisibleRows; i++)
        {
            var index = _topRow + i;
            var y = HeaderHeight + i * RowHeight;
            if (y >= bottom)
            {
                break;
            }

            var selected = index == _selectedRow;
            if (selected)
            {
                g.FillRectangle(selectedBrush, 0, y, Width, RowHeight);
            }
            else if (index % 2 == 1)
            {
                g.FillRectangle(altBrush, 0, y, Width, RowHeight);
            }

            if (index < rows.Count)
            {
                DrawRecord(g, rows[index], y, selected ? selectedText : whiteBrush);
            }

            g.DrawLine(gridPen, 0, y + RowHeight, Width, y + RowHeight);
        }

        for (var c = 1; c < Headers.Length; c++)
        {
            g.DrawLine(gridPen, c * ColumnWidth, 0, c * ColumnWidth, Height);
        }
    }

    private void DrawRecord(Graphics g, CloseRecord record, int y, Brush brush)
    {
        var values = new[]
        {
            record.Time.ToString("HH:mm:ss"),
            record.InstrumentId,
            record.Direction == PositionDirection.Long ? "多" : "空",
            record.OpenPrice.ToString("0.###"),
            record.ClosePrice.ToString("0.###"),
            record.Volume.ToString(),
            record.Pnl.ToString("+0.##;-0.##;0"),
        };

        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        for (var c = 0; c < values.Length; c++)
        {
            var rect = new Rectangle(c * ColumnWidth + 2, y, ColumnWidth - 4, RowHeight);
            if (c == values.Length - 1)
            {
                using var pnlBrush = new SolidBrush(record.Pnl >= 0
                    ? (brush == SystemBrushes.HighlightText ? Color.White : Color.FromArgb(0, 130, 0))
                    : (brush == SystemBrushes.HighlightText ? Color.White : Color.FromArgb(190, 30, 30)));
                g.DrawString(values[c], _font, pnlBrush, rect, format);
            }
            else
            {
                g.DrawString(values[c], _font, brush, rect, format);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.Y < HeaderHeight)
        {
            return;
        }

        _selectedRow = _topRow + (e.Y - HeaderHeight) / RowHeight;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var step = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        _topRow -= Math.Sign(e.Delta) * step;
        if (_topRow < 0)
        {
            _topRow = 0;
        }

        var rows = RecordsProvider?.Invoke() ?? [];
        var maxTop = Math.Max(0, Math.Min(rows.Count, RowCount) - VisibleRows);
        if (_topRow > maxTop)
        {
            _topRow = maxTop;
        }

        Invalidate();
    }
}
