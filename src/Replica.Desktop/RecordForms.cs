namespace Replica.Desktop;

internal sealed record RecordEntry(string Text, Color ForeColor, Color BackColor);

/// <summary>
/// 原软件 TSoundWin（涨跌记录，205×564）与 TJCJLWin（价差记录，334×564）的复刻：
/// 窗体只有一个 alClient 的 lbOwnerDrawFixed 列表框，逐行按颜色绘制。
/// </summary>
internal sealed class RecordListForm : Form
{
    private readonly Func<IReadOnlyList<RecordEntry>> _provider;
    private readonly RecordListBox _list = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    internal RecordListForm(
        string caption,
        int width,
        int height,
        string listName,
        Func<IReadOnlyList<RecordEntry>> provider)
    {
        _provider = provider;

        Text = caption;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(width), Theme.S(height));
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = SystemColors.Control;
        Font = Theme.F(17, false, "MS Sans Serif");

        _list.Name = listName;
        _list.Dock = DockStyle.Fill;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = Theme.S(20);
        _list.Font = Theme.F(20, false, "新宋体");
        _list.BorderStyle = BorderStyle.None;
        _list.IntegralHeight = false;
        Controls.Add(_list);

        RefreshItems();
        _timer.Tick += (_, _) => RefreshItems();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private void RefreshItems()
    {
        if (IsDisposed)
        {
            return;
        }

        var entries = _provider();
        if (entries.Count == _list.Items.Count && entries.Count > 0)
        {
            var same = true;
            for (var i = 0; i < entries.Count; i++)
            {
                if (_list.Items[i] is not RecordEntry existing || !string.Equals(existing.Text, entries[i].Text))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        var top = _list.TopIndex;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in entries)
        {
            _list.Items.Add(entry);
        }

        _list.EndUpdate();
        if (top >= 0 && top < _list.Items.Count)
        {
            _list.TopIndex = top;
        }
    }

    private sealed class RecordListBox : ListBox
    {
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                return;
            }

            var entry = (RecordEntry)Items[e.Index]!;
            using var back = new SolidBrush(entry.BackColor);
            e.Graphics.FillRectangle(back, e.Bounds);

            using var fore = new SolidBrush(entry.ForeColor);
            var format = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            e.Graphics.DrawString(entry.Text, Font, fore, e.Bounds, format);
        }
    }
}

/// <summary>
/// 原软件 TMinuteBarsWin 的复刻：690×861，SGMinuteBars（7 列、200 行、无固定列）。
/// 这里记录每次行情推送的最新价/买卖价/成交量/持仓量。
/// </summary>
internal sealed class MinuteBarsForm : Form
{
    private readonly Replica.Core.Services.TradingSession _session;
    private readonly DataGridView _grid = new();
    private readonly List<Row> _rows = [];
    private readonly object _sync = new();

    private sealed record Row(
        string Time,
        string InstrumentId,
        double Last,
        double Bid,
        double Ask,
        double Volume,
        double OpenInterest);

    internal MinuteBarsForm(Replica.Core.Services.TradingSession session)
    {
        _session = session;

        Text = "行情推送记录";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(690), Theme.S(861));
        StartPosition = FormStartPosition.CenterParent;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        _grid.Name = "SGMinuteBars";
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToResizeRows = false;
        _grid.BackgroundColor = Color.FromArgb(200, 200, 200);
        _grid.Font = Theme.F(18, false, "新宋体");
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = Theme.S(25);

        _grid.Columns.Add("Time", "时间");
        _grid.Columns.Add("Id", "合约");
        _grid.Columns.Add("Last", "最新价");
        _grid.Columns.Add("Bid", "买价");
        _grid.Columns.Add("Ask", "卖价");
        _grid.Columns.Add("Volume", "成交量");
        _grid.Columns.Add("Oi", "持仓量");
        Controls.Add(_grid);

        _session.QuoteUpdated += OnQuoteUpdated;
        FormClosed += (_, _) => _session.QuoteUpdated -= OnQuoteUpdated;
    }

    private void OnQuoteUpdated(object? sender, Replica.Core.Models.Quote quote)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                lock (_sync)
                {
                    _rows.Insert(0, new Row(
                        quote.UpdateTime == default ? DateTime.Now.ToString("HH:mm:ss") : quote.UpdateTime.ToString("HH:mm:ss"),
                        quote.InstrumentId,
                        quote.LastPrice,
                        quote.BidPrice,
                        quote.AskPrice,
                        quote.Volume,
                        quote.OpenInterest));

                    if (_rows.Count > 200)
                    {
                        _rows.RemoveRange(200, _rows.Count - 200);
                    }
                }

                Render();
            });
        }
        catch (InvalidOperationException)
        {
            // 窗体正在关闭。
        }
    }

    private void Render()
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        lock (_sync)
        {
            foreach (var row in _rows)
            {
                _grid.Rows.Add(
                    row.Time,
                    row.InstrumentId,
                    row.Last.ToString("0.###"),
                    row.Bid.ToString("0.###"),
                    row.Ask.ToString("0.###"),
                    row.Volume.ToString("0"),
                    row.OpenInterest.ToString("0"));
            }
        }

        _grid.ResumeLayout();
    }
}
