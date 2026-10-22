using System.Media;
using Replica.Core.Models;

namespace Replica.Desktop;

internal sealed record AlertRecord(
    DateTime Time,
    string InstrumentId,
    double Price,
    double ChangePercent,
    double VolumeDelta,
    double Change);

/// <summary>合约监控与警报窗之间共享的警报队列。</summary>
internal sealed class AlertHub
{
    public static AlertHub Instance { get; } = new();

    private readonly List<AlertRecord> _records = [];
    private readonly object _sync = new();

    public event EventHandler<AlertRecord>? Alerted;

    public IReadOnlyList<AlertRecord> Snapshot()
    {
        lock (_sync)
        {
            return _records.ToArray();
        }
    }

    public void Add(AlertRecord record)
    {
        lock (_sync)
        {
            _records.Add(record);
            if (_records.Count > 500)
            {
                _records.RemoveRange(0, _records.Count - 500);
            }
        }

        Alerted?.Invoke(this, record);
    }
}

/// <summary>
/// 原软件 TMonitoredWin 的复刻：919×943，SGMonitoredInstrumentsID（7 列、200 行）
/// + 底部「合约 / 刻度S / 周期 / 阈值 / 预警量 / 添加修改 / 警报窗」面板。
/// 按周期采样行情，变动超过阈值或成交量增量超过预警量时写入警报窗。
/// </summary>
internal sealed class MonitorForm : Form
{
    private sealed class Rule
    {
        public required string InstrumentId { get; init; }
        public double Tick { get; set; }
        public int PeriodSeconds { get; set; }
        public double Threshold { get; set; }
        public double AlertVolume { get; set; }
        public DateTime SampledAt { get; set; } = DateTime.Now;
        public double BasePrice { get; set; }
        public double BaseVolume { get; set; }
    }

    private readonly ReplicaContext _ctx;
    private readonly List<Rule> _rules = [];
    private readonly DataGridView _grid = new();
    private readonly TextBox _instrument = new();
    private readonly TextBox _tick = new();
    private readonly TextBox _period = new();
    private readonly TextBox _threshold = new();
    private readonly TextBox _alertVolume = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private AlertForm? _alert;

    internal MonitorForm(ReplicaContext context)
    {
        _ctx = context;

        Text = "合约监控";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(919), Theme.S(943));
        StartPosition = FormStartPosition.CenterParent;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        BuildGrid();
        BuildPanel();

        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private void BuildGrid()
    {
        _grid.Name = "SGMonitoredInstrumentsID";
        _grid.Location = new Point(0, 0);
        _grid.Size = new Size(Theme.S(919), Theme.S(900));
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Color.FromArgb(200, 200, 200);
        _grid.Font = Theme.F(24, false, "新宋体");
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = Theme.S(25);

        _grid.Columns.Add("Id", "合约");
        _grid.Columns.Add("Tick", "刻度S");
        _grid.Columns.Add("Period", "周期");
        _grid.Columns.Add("Threshold", "阈值");
        _grid.Columns.Add("AlertVolume", "预警量");
        _grid.Columns.Add("Last", "最新价");
        _grid.Columns.Add("Change", "变动");

        _grid.CellClick += (_, e) => LoadRow(e.RowIndex);
        _grid.CellDoubleClick += (_, e) => RemoveRow(e.RowIndex);
        Controls.Add(_grid);
    }

    private void BuildPanel()
    {
        var panel = new Panel
        {
            Name = "Panel1",
            Dock = DockStyle.Bottom,
            Height = Theme.S(43),
            BackColor = SystemColors.Control,
        };
        Controls.Add(panel);

        var labelFont = Theme.F(24, false, "新宋体");
        var labelFore = Color.Teal;

        PanelLabel(panel, "Label4", 0, 10, 48, 24, "合约", labelFont, labelFore);
        PanelLabel(panel, "Label1", 210, 10, 60, 24, "刻度S", labelFont, labelFore);
        PanelLabel(panel, "Label2", 340, 10, 48, 24, "周期", labelFont, labelFore);
        PanelLabel(panel, "Label3", 450, 10, 48, 24, "阈值", labelFont, labelFore);
        PanelLabel(panel, "Label5", 565, 10, 72, 24, "预警量", labelFont, labelFore);

        PanelEdit(panel, "TxHYID", 58, 8, 147, 28, _instrument, string.Empty);
        PanelEdit(panel, "TxIntervalS", 280, 8, 50, 28, _tick, "1");
        PanelEdit(panel, "TxPeriod", 395, 8, 50, 28, _period, "60");
        PanelEdit(panel, "TxThreshold", 500, 8, 60, 28, _threshold, "100");
        PanelEdit(panel, "TxAlertVolume", 645, 8, 60, 28, _alertVolume, "100");

        var add = Theme.MakeButton(panel, "BtAddHY", Theme.S(710), Theme.S(7), Theme.S(111), Theme.S(33),
            "添加/修改", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        add.Click += (_, _) => AddOrUpdate();

        var alert = Theme.MakeButton(panel, "Button2", Theme.S(835), Theme.S(7), Theme.S(70), Theme.S(33),
            "警报窗", Theme.F(20, false, "新宋体"), Theme.Delphi(16233578));
        alert.Click += (_, _) => OpenAlertWindow();
    }

    private static void PanelLabel(
        Control parent,
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore) =>
        Theme.MakeLabel(parent, name, Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height), text, font, fore);

    private static void PanelEdit(
        Control parent,
        string name,
        int x,
        int y,
        int width,
        int height,
        TextBox target,
        string text)
    {
        target.Name = name;
        target.Left = Theme.S(x);
        target.Top = Theme.S(y);
        target.Width = Theme.S(width);
        target.Height = Theme.S(height);
        target.Font = Theme.F(20, false, "新宋体");
        target.ForeColor = Color.Maroon;
        target.BorderStyle = BorderStyle.FixedSingle;
        target.Text = text;
        parent.Controls.Add(target);
    }

    private void AddOrUpdate()
    {
        var id = _instrument.Text.Trim().ToUpperInvariant();
        if (id.Length == 0 || _ctx.Instruments.Find(id) is null)
        {
            _ctx.Log.Append($"监控：合约 {id} 不存在。");
            return;
        }

        var rule = _rules.FirstOrDefault(r =>
            string.Equals(r.InstrumentId, id, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            rule = new Rule { InstrumentId = id };
            _rules.Add(rule);
        }

        rule.Tick = ParseDouble(_tick.Text, 1);
        rule.PeriodSeconds = Math.Clamp((int)ParseDouble(_period.Text, 60), 1, 3600);
        rule.Threshold = ParseDouble(_threshold.Text, 100);
        rule.AlertVolume = ParseDouble(_alertVolume.Text, 100);
        var quote = _ctx.Session.GetQuote(id);
        rule.BasePrice = quote?.LastPrice ?? 0;
        rule.BaseVolume = quote?.Volume ?? 0;
        rule.SampledAt = DateTime.Now;

        RenderRules();
        _ctx.Log.Append($"监控：{id} 周期 {rule.PeriodSeconds}s 阈值 {rule.Threshold} 预警量 {rule.AlertVolume}");
    }

    private void RemoveRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rules.Count)
        {
            return;
        }

        var removed = _rules[rowIndex];
        _rules.RemoveAt(rowIndex);
        RenderRules();
        _ctx.Log.Append($"监控：移除 {removed.InstrumentId}。");
    }

    private void LoadRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rules.Count)
        {
            return;
        }

        var rule = _rules[rowIndex];
        _instrument.Text = rule.InstrumentId;
        _tick.Text = rule.Tick.ToString("0.###");
        _period.Text = rule.PeriodSeconds.ToString();
        _threshold.Text = rule.Threshold.ToString("0.###");
        _alertVolume.Text = rule.AlertVolume.ToString("0.###");
    }

    private void Sample()
    {
        if (_rules.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            var quote = _ctx.Session.GetQuote(rule.InstrumentId);
            if (quote is null)
            {
                continue;
            }

            if (i < _grid.Rows.Count)
            {
                _grid.Rows[i].Cells[5].Value = quote.LastPrice.ToString("0.###");
            }

            if ((DateTime.Now - rule.SampledAt).TotalSeconds < rule.PeriodSeconds)
            {
                continue;
            }

            var change = quote.LastPrice - rule.BasePrice;
            var percent = rule.BasePrice > 0 ? change / rule.BasePrice * 100 : 0;
            var volumeDelta = quote.Volume - rule.BaseVolume;

            if (i < _grid.Rows.Count)
            {
                _grid.Rows[i].Cells[6].Value = $"{percent:+0.00;-0.00;0.00}%";
            }

            var hitThreshold = rule.Threshold > 0 && Math.Abs(change) >= rule.Threshold;
            var hitVolume = rule.AlertVolume > 0 && volumeDelta >= rule.AlertVolume;
            if (hitThreshold || hitVolume)
            {
                AlertHub.Instance.Add(new AlertRecord(
                    DateTime.Now,
                    rule.InstrumentId,
                    quote.LastPrice,
                    percent,
                    volumeDelta,
                    change));
                SystemSounds.Exclamation.Play();
            }

            rule.BasePrice = quote.LastPrice;
            rule.BaseVolume = quote.Volume;
            rule.SampledAt = DateTime.Now;
        }
    }

    private void RenderRules()
    {
        _grid.Rows.Clear();
        foreach (var rule in _rules)
        {
            var quote = _ctx.Session.GetQuote(rule.InstrumentId);
            _grid.Rows.Add(
                rule.InstrumentId,
                rule.Tick.ToString("0.###"),
                rule.PeriodSeconds.ToString(),
                rule.Threshold.ToString("0.###"),
                rule.AlertVolume.ToString("0.###"),
                quote?.LastPrice.ToString("0.###") ?? "-",
                "-");
        }
    }

    private void OpenAlertWindow()
    {
        if (_alert is { IsDisposed: false })
        {
            _alert.Activate();
            return;
        }

        _alert = new AlertForm(_ctx);
        _alert.FormClosed += (_, _) => _alert = null;
        _alert.Show(this);
    }

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, out var result) ? result : fallback;
}

/// <summary>
/// 原软件 TAlertWin 的复刻：997×893，左侧 SGAlert（4 列、200 行）
/// + 右侧 LVMonitorHistory（时间/合约/预警值/变动率/预警量/变动量）。
/// </summary>
internal sealed class AlertForm : Form
{
    private readonly ReplicaContext _ctx;
    private readonly DataGridView _grid = new();
    private readonly ListView _history = new();
    private readonly int _maxRows = 200;

    internal AlertForm(ReplicaContext context)
    {
        _ctx = context;

        Text = "警报窗";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(997), Theme.S(893));
        StartPosition = FormStartPosition.CenterParent;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        BuildGrid();
        BuildHistory();

        foreach (var record in AlertHub.Instance.Snapshot())
        {
            Append(record);
        }

        AlertHub.Instance.Alerted += OnAlerted;
        FormClosed += (_, _) => AlertHub.Instance.Alerted -= OnAlerted;
    }

    private void BuildGrid()
    {
        _grid.Name = "SGAlert";
        _grid.Location = new Point(0, 0);
        _grid.Size = new Size(Theme.S(413), Theme.S(893));
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.FromArgb(200, 200, 200);
        _grid.Font = Theme.F(24, false, "新宋体");
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = Theme.S(25);

        _grid.Columns.Add("Time", "时间");
        _grid.Columns.Add("Id", "合约");
        _grid.Columns.Add("Price", "预警值");
        _grid.Columns.Add("Percent", "变动率");
        Controls.Add(_grid);
    }

    private void BuildHistory()
    {
        _history.Name = "LVMonitorHistory";
        _history.Left = Theme.S(496);
        _history.Top = Theme.S(-1);
        _history.Width = Theme.S(535);
        _history.Height = Theme.S(903);
        _history.View = View.Details;
        _history.GridLines = true;
        _history.FullRowSelect = true;
        _history.Font = Theme.F(24, false, "新宋体");
        _history.Columns.Add("时间", Theme.S(90));
        _history.Columns.Add("合约", Theme.S(90));
        _history.Columns.Add("预警值", Theme.S(80));
        _history.Columns.Add("变动率", Theme.S(80));
        _history.Columns.Add("预警量", Theme.S(80));
        _history.Columns.Add("变动量", Theme.S(80));
        Controls.Add(_history);
    }

    private void OnAlerted(object? sender, AlertRecord record)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => Append(record));
        }
        catch (InvalidOperationException)
        {
            // 窗体正在关闭。
        }
    }

    private void Append(AlertRecord record)
    {
        var percent = $"{record.ChangePercent:+0.00;-0.00;0.00}%";
        _grid.Rows.Insert(0, record.Time.ToString("HH:mm:ss"), record.InstrumentId,
            record.Price.ToString("0.###"), percent);

        var item = new ListViewItem(record.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(record.InstrumentId);
        item.SubItems.Add(record.Price.ToString("0.###"));
        item.SubItems.Add(percent);
        item.SubItems.Add(record.VolumeDelta.ToString("0"));
        item.SubItems.Add(record.Change.ToString("+0.###;-0.###;0"));
        item.ForeColor = record.Change >= 0 ? Color.Firebrick : Color.SeaGreen;
        _history.Items.Insert(0, item);

        while (_grid.Rows.Count > _maxRows)
        {
            _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
        }

        while (_history.Items.Count > _maxRows)
        {
            _history.Items.RemoveAt(_history.Items.Count - 1);
        }
    }
}
