namespace Replica.Desktop;

/// <summary>
/// 原软件 TInstrumentsWin 的复刻：合约列表。
/// 在保留 ID / Name 两列的基础上，增加交易所、最新价、涨跌幅与搜索过滤，
/// 双击或回车把合约送入主窗行情梯。
/// </summary>
internal sealed class InstrumentsForm : Form
{
    private readonly MainForm _main;
    private readonly ReplicaContext _ctx;
    private readonly ListView _list = new();
    private readonly TextBox _search = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    internal InstrumentsForm(MainForm main, ReplicaContext context)
    {
        _main = main;
        _ctx = context;

        Text = "合约列表";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(560), Theme.S(620));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.S(40),
            BackColor = SystemColors.Control,
        };

        _search.Parent = header;
        _search.SetBounds(Theme.S(6), Theme.S(6), Theme.S(320), Theme.S(28));
        _search.Font = Theme.F(20);
        _search.PlaceholderText = "输入合约代码 / 名称过滤";
        _search.TextChanged += (_, _) => Populate();

        var tip = new Label
        {
            Parent = header,
            Text = "双击送入行情梯",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.DimGray,
            Font = Theme.F(18),
        };
        tip.SetBounds(Theme.S(330), Theme.S(6), Theme.S(220), Theme.S(28));

        _status.Dock = DockStyle.Bottom;
        _status.Height = Theme.S(26);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = Theme.F(18);
        _status.ForeColor = Color.DimGray;
        _status.Padding = new Padding(Theme.S(6), 0, 0, 0);

        _list.Name = "LvHyList";
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.GridLines = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Font = Theme.F(24, false, "新宋体");
        _list.Columns.Add("ID", Theme.S(120), HorizontalAlignment.Left);
        _list.Columns.Add("Name", Theme.S(170), HorizontalAlignment.Left);
        _list.Columns.Add("交易所", Theme.S(80), HorizontalAlignment.Left);
        _list.Columns.Add("最新价", Theme.S(90), HorizontalAlignment.Right);
        _list.Columns.Add("涨跌幅", Theme.S(80), HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => SendSelected();

        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendSelected();
                e.Handled = true;
            }
        };

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(_status);

        Populate();

        _timer.Tick += (_, _) => RefreshQuotes();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private void Populate()
    {
        var keyword = _search.Text.Trim();
        var items = _ctx.Instruments.All
            .Where(i => keyword.Length == 0 ||
                        i.InstrumentId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        (i.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.InstrumentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var instrument in items)
        {
            var item = new ListViewItem(instrument.InstrumentId);
            item.SubItems.Add(instrument.Name ?? string.Empty);
            item.SubItems.Add(instrument.ExchangeId ?? string.Empty);
            item.SubItems.Add("—");
            item.SubItems.Add("—");
            item.Tag = instrument.InstrumentId;
            _list.Items.Add(item);
        }

        _list.EndUpdate();
        _status.Text = $"共 {_ctx.Instruments.All.Count} 个合约，当前显示 {items.Length} 个";
        RefreshQuotes();
    }

    private void RefreshQuotes()
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        _list.BeginUpdate();
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is not string id)
            {
                continue;
            }

            var quote = _ctx.Session.GetQuote(id);
            if (quote is null)
            {
                continue;
            }

            item.SubItems[3].Text = quote.LastPrice.ToString("0.##");
            if (quote.PreSettlementPrice > 0)
            {
                var pct = (quote.LastPrice - quote.PreSettlementPrice) / quote.PreSettlementPrice;
                item.SubItems[4].Text = pct.ToString("+0.00%;-0.00%;0.00%");
                item.SubItems[4].ForeColor = pct > 0 ? Color.Firebrick : pct < 0 ? Color.SeaGreen : Color.Black;
            }
        }

        _list.EndUpdate();
    }

    private void SendSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        var id = _list.SelectedItems[0].Text;
        _main.ActivateInstrument(id);
        _ctx.Log.Append($"合约列表：送入 {id}。");
        Close();
    }
}
