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
    private readonly Label _count = new();
    private readonly CheckBox _freeze = new();
    private readonly Button _copy = new();
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

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.S(26),
            BackColor = SystemColors.Control,
        };

        _count.Parent = header;
        _count.SetBounds(Theme.S(4), 0, Theme.S(78), Theme.S(24));
        _count.Font = Theme.F(17, false, "新宋体");
        _count.ForeColor = Color.DimGray;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Text = "共 0 条";

        _freeze.Parent = header;
        _freeze.SetBounds(Theme.S(82), Theme.S(2), Theme.S(58), Theme.S(22));
        _freeze.Text = "冻结";
        _freeze.Font = Theme.F(17, false, "新宋体");
        _freeze.ForeColor = Color.Teal;
        _freeze.CheckedChanged += (_, _) => _timer.Enabled = !_freeze.Checked;

        _copy.Parent = header;
        _copy.SetBounds(Theme.S(142), Theme.S(1), Theme.S(58), Theme.S(23));
        _copy.Text = "复制";
        _copy.Font = Theme.F(17, false, "新宋体");
        _copy.ForeColor = Color.Maroon;
        _copy.FlatStyle = FlatStyle.System;
        _copy.TabStop = false;
        _copy.Click += (_, _) => CopySelected();

        _list.Name = listName;
        _list.Dock = DockStyle.Fill;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = Theme.S(20);
        _list.Font = Theme.F(20, false, "新宋体");
        _list.BorderStyle = BorderStyle.None;
        _list.IntegralHeight = false;
        _list.DoubleClick += (_, _) => CopySelected();
        Controls.Add(_list);
        Controls.Add(header);

        RefreshItems();
        _timer.Tick += (_, _) => RefreshItems();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private void CopySelected()
    {
        if (_list.SelectedItem is not RecordEntry entry)
        {
            return;
        }

        try
        {
            Clipboard.SetText(entry.Text);
        }
        catch (Exception)
        {
            // 剪贴板被其他进程占用时忽略。
        }
    }

    private void RefreshItems()
    {
        if (IsDisposed)
        {
            return;
        }

        var entries = _provider();
        _count.Text = $"共 {entries.Count} 条";
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
    private readonly Label _count = new();
    private readonly CheckBox _pause = new();
    private readonly Button _clear = new();
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
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 246, 246);

        _grid.Columns.Add("Time", "时间");
        _grid.Columns.Add("Id", "合约");
        _grid.Columns.Add("Last", "最新价");
        _grid.Columns.Add("Bid", "买价");
        _grid.Columns.Add("Ask", "卖价");
        _grid.Columns.Add("Volume", "成交量");
        _grid.Columns.Add("Oi", "持仓量");

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.S(30),
            BackColor = SystemColors.Control,
        };

        _count.Parent = header;
        _count.SetBounds(Theme.S(6), 0, Theme.S(240), Theme.S(28));
        _count.Font = Theme.F(18, false, "新宋体");
        _count.ForeColor = Color.DimGray;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Text = "共 0 条（上限 200）";

        _pause.Parent = header;
        _pause.SetBounds(Theme.S(500), Theme.S(4), Theme.S(70), Theme.S(24));
        _pause.Text = "暂停";
        _pause.Font = Theme.F(18, false, "新宋体");
        _pause.ForeColor = Color.Teal;
        _pause.CheckedChanged += (_, _) =>
        {
            if (!_pause.Checked)
            {
                RenderAll();
            }
        };

        _clear.Parent = header;
        _clear.SetBounds(Theme.S(578), Theme.S(3), Theme.S(80), Theme.S(25));
        _clear.Text = "清空";
        _clear.Font = Theme.F(18, false, "新宋体");
        _clear.ForeColor = Color.Maroon;
        _clear.FlatStyle = FlatStyle.System;
        _clear.TabStop = false;
        _clear.Click += (_, _) =>
        {
            lock (_sync)
            {
                _rows.Clear();
            }

            _grid.Rows.Clear();
            _count.Text = "共 0 条（上限 200）";
        };

        Controls.Add(_grid);
        Controls.Add(header);

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
                var row = new Row(
                    quote.UpdateTime == default ? DateTime.Now.ToString("HH:mm:ss") : quote.UpdateTime.ToString("HH:mm:ss"),
                    quote.InstrumentId,
                    quote.LastPrice,
                    quote.BidPrice,
                    quote.AskPrice,
                    quote.Volume,
                    quote.OpenInterest);

                lock (_sync)
                {
                    _rows.Insert(0, row);
                    if (_rows.Count > 200)
                    {
                        _rows.RemoveAt(_rows.Count - 1);
                    }
                }

                _count.Text = $"共 {_rows.Count} 条（上限 200）";
                if (_pause.Checked)
                {
                    return;
                }

                InsertRow(row);
            });
        }
        catch (InvalidOperationException)
        {
            // 窗体正在关闭。
        }
    }

    private void InsertRow(Row row)
    {
        var index = _grid.Rows.Add(
            row.Time,
            row.InstrumentId,
            row.Last.ToString("0.###"),
            row.Bid.ToString("0.###"),
            row.Ask.ToString("0.###"),
            row.Volume.ToString("0"),
            row.OpenInterest.ToString("0"));

        var added = _grid.Rows[index];
        if (row.Last >= row.Ask && row.Ask > 0)
        {
            added.Cells[2].Style.ForeColor = Color.Firebrick;
        }
        else if (row.Last <= row.Bid && row.Bid > 0)
        {
            added.Cells[2].Style.ForeColor = Color.SeaGreen;
        }

        while (_grid.Rows.Count > 200)
        {
            _grid.Rows.RemoveAt(0);
        }
    }

    private void RenderAll()
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        lock (_sync)
        {
            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                var row = _rows[i];
                var index = _grid.Rows.Add(
                    row.Time,
                    row.InstrumentId,
                    row.Last.ToString("0.###"),
                    row.Bid.ToString("0.###"),
                    row.Ask.ToString("0.###"),
                    row.Volume.ToString("0"),
                    row.OpenInterest.ToString("0"));

                if (row.Last >= row.Ask && row.Ask > 0)
                {
                    _grid.Rows[index].Cells[2].Style.ForeColor = Color.Firebrick;
                }
                else if (row.Last <= row.Bid && row.Bid > 0)
                {
                    _grid.Rows[index].Cells[2].Style.ForeColor = Color.SeaGreen;
                }
            }
        }

        _grid.ResumeLayout();
    }
}
