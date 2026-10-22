using System.Media;
using Replica.Core.Models;
using Replica.Core.Services;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Replica.Desktop;

/// <summary>
/// 主窗体，严格按原软件 TYYWin 的 DFM 布局重建：
/// 左侧 131px 信息栏 + 右侧 5 列行情梯（SGPriceList）+ 一整列开关标签。
/// 不包含授权状态、云端心跳、MAC 校验与版本校验。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly ReplicaContext _ctx;
    private readonly TradingSession _session;
    private readonly InstrumentRepository _instruments;
    private readonly LogHub _log;

    private readonly PriceLadder _ladder = new();
    private readonly WinFormsTimer _clock = new() { Interval = 250 };

    private readonly Dictionary<string, double> _lastPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string InstrumentId, PositionDirection Direction), Position> _positions = [];
    private readonly List<string> _contracts = [];
    private readonly List<Control> _hiddenExtras = [];

    private Label _lbCcyk = null!;
    private Label _lbPcyk = null!;
    private Label _lbLoadFlag = null!;
    private Label _lbData = null!;
    private Label _lbTimer = null!;
    private Label _lbZdf = null!;
    private Label _lbRed = null!;
    private Label _lbBlue = null!;
    private Label _lbHqCount = null!;
    private Label _lbKp = null!;
    private Label _lbPt = null!;
    private Label _lbFct = null!;
    private Label _lbPzjt = null!;
    private Label _lbJingdu = null!;
    private Label _lbJt = null!;
    private Label _lbSyMode = null!;
    private Label _lbCjJc = null!;
    private Label _lbCjSyMode = null!;
    private Label _lbGroup = null!;
    private Label _lbCdCount = null!;
    private Label _lbCloseYesterday = null!;
    private Label _lbClose60 = null!;
    private Label _btWinMin = null!;
    private Label _btRowDec = null!;
    private Label _btRowAdd = null!;

    private Label _cbNearby = null!;
    private Label _cbOnlyOpen = null!;
    private Label _cbCntrbySprd = null!;
    private Label _cbCntrbySprdEx = null!;
    private Label _cbJcQh = null!;
    private Label _cbCdLock = null!;
    private Label _cbZdtLock = null!;
    private Label _cbBgds = null!;
    private Label _cbCjFlag = null!;
    private Label _cbMorderX = null!;
    private Label _cbFok = null!;
    private Label _cbShow = null!;
    private Label _cbHys = null!;
    private Label _cbOc = null!;
    private Label _cbUpDown = null!;

    private TextBox _valLeft = null!;
    private TextBox _valRight = null!;
    private TextBox _txSprdId = null!;
    private TextBox _txSprdPt = null!;
    private TextBox _txSprdFctn = null!;
    private TextBox _txPriceTick = null!;
    private TextBox _txUpDown = null!;
    private RadioButton _rboA = null!;
    private RadioButton _rboB = null!;
    private Panel _pLine = null!;

    private ControlCenterForm? _console;
    private CloseRecordForm? _records;
    private PointWindow? _point;
    private RecordListForm? _soundRecords;
    private RecordListForm? _spreadRecords;
    private MinuteBarsForm? _minuteBars;

    private int _group = 1;
    private int _offsetMode = 1;
    private int _soundMode;
    private int _tradeCount;
    private int _quoteCount;
    private int _rankingMode;
    private string? _lockedColumn;
    private bool _closing;

    internal MainForm(ReplicaContext context)
    {
        _ctx = context;
        _session = context.Session;
        _instruments = context.Instruments;
        _log = context.Log;

        Text = "YYWin";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(428), Theme.S(1515));
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        MaximizeBox = false;
        KeyPreview = true;
        BackColor = Theme.FormBack;
        Font = Theme.F(18, false, "Segoe UI");
        DoubleBuffered = true;

        BuildLayout();
        WireSession();
        ApplyGroup();
        UpdateInfo();

        _clock.Tick += (_, _) => OnClockTick();
        _clock.Start();
        OnClockTick();

        Shown += (_, _) =>
        {
            PositionWindow();
            OpenPointWindow();
        };
    }

    internal TradingSession Session => _session;

    internal InstrumentRepository Instruments => _instruments;

    internal LogHub Log => _log;

    internal ReplicaContext Context => _ctx;

    internal string? ActiveInstrument => _ladder.ActiveInstrument;

    internal IReadOnlyList<string> Contracts => _contracts;

    // ---------------------------------------------------------------- 布局

    private void BuildLayout()
    {
        _ladder.QuoteProvider = id => _session.GetQuote(id);
        _ladder.InstrumentProvider = id => _instruments.Find(id);
        _ladder.OrderProvider = WorkingOrders;
        _ladder.PositionProvider = PositionsOf;
        _ladder.CellClicked += OnLadderCellClicked;
        _ladder.ColumnActivated += OnColumnActivated;
        _ladder.Depth = 10;
        _ladder.WheelRows = 1;
        _ladder.SetBounds(Theme.S(132), 0, Theme.S(300), ClientSize.Height);
        _ladder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        Controls.Add(_ladder);

        _lbHqCount = Lbl("LBHQCount", 50, 2, 28, 18, "0", Theme.F(16, true), Theme.Delphi(7895260));
        _lbHqCount.TextAlign = ContentAlignment.MiddleRight;

        _btWinMin = ClickLabel("BtWinMin", 0, 0, 23, 23, "_ ", Theme.F(18, true), Color.Black, Theme.MoneyGreen,
            ContentAlignment.MiddleCenter);
        _btWinMin.Click += (_, _) => WindowState = FormWindowState.Minimized;

        _btRowDec = ClickLabel("BtRowDec", 78, 0, 23, 23, "-", Theme.F(18, true), Color.Black, Theme.MoneyGreen,
            ContentAlignment.MiddleCenter);
        _btRowDec.Click += (_, _) => ChangeDepth(-1);

        _btRowAdd = ClickLabel("BtRowAdd", 105, 0, 23, 23, "+", Theme.F(18, true), Color.Black, Theme.MoneyGreen,
            ContentAlignment.MiddleCenter);
        _btRowAdd.Click += (_, _) => ChangeDepth(1);

        _lbCloseYesterday = ClickLabel("LBCloseYesterday", 26, 1, 23, 23, "昨", Theme.F(18, true), Color.Black,
            Color.Yellow, ContentAlignment.MiddleCenter);
        _lbCloseYesterday.Click += (_, _) => _log.Append("昨仓按钮：平昨仓模式。");
        _hiddenExtras.Add(_lbCloseYesterday);

        _lbTimer = Lbl("LBTimer", 0, 26, 131, 28, "00:00:00", Theme.F(26, true, "宋体"),
            Theme.Delphi(3684555), Color.Silver, ContentAlignment.MiddleCenter);
        _lbTimer.Cursor = Cursors.Hand;
        _lbTimer.DoubleClick += (_, _) =>
        {
            _clockBase = _clockBase == default ? DateTime.Now : default;
            _log.Append("时钟：切换为交易计时。");
        };

        _lbZdf = Lbl("LBZDF", 0, 56, 131, 40, "+0.00%", Theme.F(30, true, "宋体"), Color.Black, Theme.ChangeBack,
            ContentAlignment.MiddleCenter);
        _lbZdf.Cursor = Cursors.Hand;
        _lbZdf.Click += (_, _) => ShowRanking(0);

        _lbData = Lbl("lblData", 0, 96, 131, 86, "载入中", Theme.F(20, true, "宋体"), Color.Black,
            Theme.DataBack, ContentAlignment.MiddleCenter);

        _lbRed = Lbl("LBRed", 0, 183, 131, 30, "0", Theme.F(24, true, "宋体"), Color.Black, Theme.RedBack,
            ContentAlignment.MiddleCenter);
        _lbRed.Cursor = Cursors.Hand;
        _lbRed.Click += (_, _) => ShowRanking(1);

        _lbBlue = Lbl("LBBlue", 0, 216, 131, 30, "0", Theme.F(24, true, "宋体"), Color.Black, Theme.SkyBlue,
            ContentAlignment.MiddleCenter);
        _lbBlue.Cursor = Cursors.Hand;
        _lbBlue.Click += (_, _) => ShowRanking(2);

        _valLeft = Edit("ValLeft", 0, 249, 65, 32, "1", Theme.F(24, true), Color.Black, Theme.FormBack,
            ContentAlignment.MiddleCenter);
        _valRight = Edit("ValRight", 65, 249, 66, 32, "2", Theme.F(24, true), Color.Black, Theme.FormBack,
            ContentAlignment.MiddleCenter);
        NumericOnly(_valLeft);
        NumericOnly(_valRight);

        _rboA = Radio("RBOA", 11, 287, 46, 25, "A", true);
        _rboB = Radio("RBOB", 75, 287, 47, 25, "B", false);
        _rboA.CheckedChanged += (_, _) => UpdateInfo();
        _rboB.CheckedChanged += (_, _) => UpdateInfo();

        _cbNearby = Toggle("CBNearby", 5, 314, 116, 27, "CgNearby", true, Theme.F(20, false, "微软雅黑"),
            Color.Black);
        _cbOnlyOpen = Toggle("CBOnlyOpen", 5, 340, 114, 27, "OnlyOpen", false,
            Theme.F(20, false, "微软雅黑"), Theme.Delphi(9643042));
        _cbCntrbySprd = Toggle("CBCntrbySprd", 5, 365, 128, 27, "CntrbySprd", false,
            Theme.F(20, false, "微软雅黑"), Color.Black);

        _lbPt = Lbl("LBPt", 0, 435, 20, 20, "Pt", Theme.F(20), Color.Black);
        _lbFct = Lbl("LBFct", 0, 467, 30, 20, "Fct", Theme.F(20), Color.Black);

        _cbCdLock = Toggle("CBCDLock", 5, 500, 98, 27, "挂单锁定", false,
            Theme.F(20, false, "微软雅黑"), Theme.Delphi(7895260));
        _cbUpDown = Toggle("CBUpDown", 8, 527, 38, 27, "声", false,
            Theme.F(20, false, "微软雅黑"), Color.Maroon);
        _txUpDown = Edit("TXUpDown", 56, 530, 40, 27, "1", Theme.F(14, false, "Segoe UI"), Color.Black,
            Theme.FormBack);
        NumericOnly(_txUpDown);
        _txUpDown.TextChanged += (_, _) => _ladder.WheelRows = ParseInt(_txUpDown.Text, 1);
        _lbSyMode = ClickLabel("LBSYmode", 105, 531, 23, 23, "0", Theme.F(18, true, "宋体"), Color.Black,
            Theme.CashBack, ContentAlignment.MiddleCenter);
        _lbSyMode.Click += (_, _) => CycleSoundMode();

        _cbCntrbySprdEx = Toggle("CBCntrbySprdEX", 5, 560, 128, 27, "CntrbySprd", false,
            Theme.F(20, false, "微软雅黑"), Color.Black);
        _cbCntrbySprdEx.Click += (_, _) => _log.Append("扩展价差模式：与当前列联动。");
        _hiddenExtras.Add(_cbCntrbySprdEx);

        _lbJingdu = Lbl("LBjingdu", 5, 605, 40, 20, "深度", Theme.F(20, false, "宋体"), Color.Blue);
        _txPriceTick = Edit("TxPriceTick", 47, 599, 85, 28, "10", Theme.F(20, false, "宋体"), Color.Purple,
            Theme.FormBack);
        NumericOnly(_txPriceTick);
        _txPriceTick.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyDepth();
                e.SuppressKeyPress = true;
            }
        };
        _txPriceTick.Leave += (_, _) => ApplyDepth();

        _lbClose60 = ClickLabel("LBClose60", -5, 917, 130, 51, string.Empty, Theme.F(40, true),
            Theme.Delphi(8388863), null, ContentAlignment.MiddleCenter);
        _lbClose60.Click += (_, _) => _log.Append("60 秒平仓窗口。");
        _hiddenExtras.Add(_lbClose60);

        _txSprdId = Edit("TXCntrbySprdID", 32, 395, 99, 32, string.Empty, Theme.F(24, false, "宋体"),
            Color.Black, Color.White);
        _txSprdPt = Edit("TXCntrbySprdPT", 32, 428, 99, 32, "0", Theme.F(24, false, "宋体"), Color.Black,
            Color.White);
        _txSprdFctn = Edit("TXCntrbySprdFctn", 32, 462, 99, 32, "1", Theme.F(24, false, "宋体"), Color.Black,
            Color.White);
        NumericOnly(_txSprdPt);
        NumericOnly(_txSprdFctn);

        _lbCcyk = ClickLabel("LBccyk", 0, 900, 131, 35, "0.00", Theme.F(36, true), Color.Red, null,
            ContentAlignment.MiddleCenter);
        _lbCcyk.Click += (_, _) => _log.Append($"持仓盈亏：{_lbCcyk.Text}");

        _lbPcyk = ClickLabel("LBpcyk", 0, 1319, 131, 36, "0.00", Theme.F(30, true), Color.Black, null,
            ContentAlignment.MiddleCenter);
        _lbPcyk.Click += (_, _) => OpenCloseRecords();

        _lbJt = ClickLabel("LBJT", -4, 974, 130, 51, "0", Theme.F(53, true), Theme.Delphi(12615808), null,
            ContentAlignment.MiddleCenter);
        _lbJt.Click += (_, _) => ResetScroll();

        _lbPzjt = ClickLabel("LBPZJT", -3, 1092, 128, 51, "0", Theme.F(53, true), Theme.Delphi(16711808),
            null, ContentAlignment.MiddleCenter);
        _lbPzjt.Click += (_, _) => OpenCloseRecords();

        _lbKp = ClickLabel("LBKP", 47, 1019, 34, 65, "P", Theme.F(65, true), Theme.Delphi(4194432), null,
            ContentAlignment.MiddleCenter);
        _lbKp.Click += (_, _) => CycleStepTicks();

        _lbGroup = ClickLabel("LBGroup", 2, 1190, 120, 63, "分组: 1", Theme.F(20, false, "新宋体"),
            Color.Teal, null, ContentAlignment.MiddleLeft);
        _lbGroup.Click += (_, _) => CycleGroup();

        _lbCjSyMode = ClickLabel("LBCJSYMode", 35, 1226, 21, 19, "1", Theme.F(18, true, "宋体"), Color.Black,
            Theme.ModeBack, ContentAlignment.MiddleCenter);
        _lbCjSyMode.Click += (_, _) => CycleOffsetMode();

        _lbCdCount = ClickLabel("LBCDCount", 0, 1149, 131, 35, "0", Theme.F(32, true), Color.Gray, null,
            ContentAlignment.MiddleCenter);
        _hiddenExtras.Add(_lbCdCount);

        _lbLoadFlag = ClickLabel("LBLoadFlag", 0, 851, 131, 30, "载入中......", Theme.F(20, false, "微软雅黑"),
            Theme.Delphi(8388863), null, ContentAlignment.MiddleLeft);
        _lbLoadFlag.Click += (_, _) => ReloadContracts();

        _lbCjJc = Lbl("LBCJJC", 2, 870, 120, 41, string.Empty, Theme.F(20, false, "宋体"), Theme.Delphi(213),
            null, ContentAlignment.MiddleCenter);

        _cbJcQh = Toggle("CBJCQH", 5, 685, 98, 27, "价差切换", false, Theme.F(20, false, "微软雅黑"),
            Color.Green);
        _cbZdtLock = Toggle("CBZDTlock", 5, 715, 100, 27, "涨跌停锁", true,
            Theme.F(20, false, "微软雅黑"), Theme.Delphi(7895260));
        _cbBgds = Toggle("CBBGDS", 5, 742, 100, 27, "白格单锁", true,
            Theme.F(20, false, "微软雅黑"), Theme.Delphi(16744576));
        _cbCjFlag = Toggle("CBcjflag", 5, 775, 98, 27, "成交标识", false,
            Theme.F(20, false, "微软雅黑"), Color.Olive);
        _cbMorderX = Toggle("CBMorderX", 5, 808, 116, 27, "M-OrderX", false,
            Theme.F(20, false, "微软雅黑"), Color.Fuchsia);

        _cbFok = Toggle("CBFok", 5, 880, 58, 27, "FOK", false, Theme.F(20, false, "微软雅黑"),
            Theme.CashBack);
        _hiddenExtras.Add(_cbFok);

        _cbShow = Toggle("CBShow", 5, 1388, 98, 27, "显示功能", false, Theme.F(20, false, "微软雅黑"),
            Color.Blue);
        _cbHys = Toggle("CBHYS", 5, 1421, 78, 27, "合约锁", false, Theme.F(20, false, "微软雅黑"),
            Theme.Delphi(8388863));

        _cbOc = Toggle("CBOC", 10, 1281, 31, 27, "C", false, Theme.F(20, false, "微软雅黑"), Color.Black);
        _hiddenExtras.Add(_cbOc);

        var ptEx = Lbl("LBPtEX", 3, 651, 20, 20, "Pt", Theme.F(20), Color.Black);
        _hiddenExtras.Add(ptEx);

        var sprdPtEx = Edit("TXCntrbySprdPTEX", 32, 644, 99, 32, "0", Theme.F(24, false, "宋体"), Color.Black,
            Color.White);
        _hiddenExtras.Add(sprdPtEx);

        var sprdIdEx = Edit("TXCntrbySprdIDEX", 31, 606, 99, 32, string.Empty, Theme.F(24, false, "宋体"),
            Color.Black, Color.White);
        _hiddenExtras.Add(sprdIdEx);

        _pLine = new Panel
        {
            Left = Theme.S(131),
            Top = Theme.S(733),
            Width = Theme.S(292),
            Height = Theme.S(10),
            BackColor = Color.Black,
            Visible = false,
        };
        Controls.Add(_pLine);
        _hiddenExtras.Add(_pLine);

        foreach (var control in _hiddenExtras)
        {
            control.Visible = false;
        }

        BuildContextMenu();
    }

    private Label Lbl(
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color? back = null,
        ContentAlignment align = ContentAlignment.MiddleLeft) =>
        Theme.MakeLabel(this, name, Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height), text, font, fore,
            back, align);

    private Label ClickLabel(
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color? back,
        ContentAlignment align)
    {
        var label = Lbl(name, x, y, width, height, text, font, fore, back, align);
        label.Cursor = Cursors.Hand;
        return label;
    }

    private Label Toggle(
        string name,
        int x,
        int y,
        int width,
        int height,
        string caption,
        bool on,
        Font font,
        Color fore)
    {
        var label = Lbl(name, x, y, width, height, Theme.ToggleText(on, caption), font, fore);
        label.Tag = caption;
        label.AccessibleName = on ? "1" : "0";
        label.Cursor = Cursors.Hand;
        label.Click += (_, _) => SetToggle(label, !IsOn(label));
        return label;
    }

    private TextBox Edit(
        string name,
        int x,
        int y,
        int width,
        int height,
        string text,
        Font font,
        Color fore,
        Color back,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var edit = Theme.MakeEdit(this, name, Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height), text, font,
            fore, back, align);
        edit.MaxLength = 24;
        return edit;
    }

    private RadioButton Radio(string name, int x, int y, int width, int height, string text, bool isChecked)
    {
        var radio = new RadioButton
        {
            Name = name,
            Text = text,
            Left = Theme.S(x),
            Top = Theme.S(y),
            Width = Theme.S(width),
            Height = Theme.S(height),
            Checked = isChecked,
            Font = Theme.F(17, false, "微软雅黑"),
            BackColor = Theme.FormBack,
            ForeColor = Color.Black,
            TabStop = true,
        };
        Controls.Add(radio);
        return radio;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(MenuItem("控制台  F1", (_, _) => OpenConsole()));
        menu.Items.Add(MenuItem("平仓记录  F2", (_, _) => OpenCloseRecords()));
        menu.Items.Add(MenuItem("账号编辑器  F3", (_, _) => OpenUserEditor()));
        menu.Items.Add(MenuItem("点价窗", (_, _) => OpenPointWindow()));
        menu.Items.Add(MenuItem("合约列表", (_, _) => OpenInstruments()));
        menu.Items.Add(MenuItem("合约监控", (_, _) => OpenMonitor()));
        menu.Items.Add(MenuItem("警报窗", (_, _) => OpenAlertWindow()));
        menu.Items.Add(MenuItem("配置", (_, _) => OpenConfig()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("涨跌记录", (_, _) => OpenSoundRecords()));
        menu.Items.Add(MenuItem("价差记录", (_, _) => OpenSpreadRecords()));
        menu.Items.Add(MenuItem("行情推送记录", (_, _) => OpenMinuteBars()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("全部撤单  F8", async (_, _) => await CancelAllOrdersAsync()));
        menu.Items.Add(MenuItem("全部平仓  F9", async (_, _) => await CloseAllPositionsAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("保存退出", (_, _) => Close()));
        ContextMenuStrip = menu;
    }

    private static ToolStripMenuItem MenuItem(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    // ---------------------------------------------------------------- 状态

    private DateTime _clockBase;

    private void OnClockTick()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        _lbTimer.Text = _clockBase == default
            ? DateTime.Now.ToString("HH:mm:ss")
            : (DateTime.Now - _clockBase).ToString(@"hh\:mm\:ss");

        UpdateInfo();
    }

    private void WireSession()
    {
        _session.QuoteUpdated += OnQuoteUpdated;
        _session.OrderUpdated += OnOrderUpdated;
        _session.TradeReceived += OnTradeReceived;
        _session.PositionUpdated += OnPositionUpdated;
        _session.Log += (_, message) => _log.Append(message);
    }

    private void UpdateInfo()
    {
        if (_contracts.Count == 0)
        {
            _lbLoadFlag.Text = "无合约";
            return;
        }

        var active = _ladder.ActiveInstrument ?? _contracts[0];
        var instrument = _instruments.Find(active);
        var quote = _session.GetQuote(active);

        _lbData.Text = instrument is null
            ? active
            : $"{instrument.Name}\n{instrument.InstrumentId}";

        if (quote is not null)
        {
            var reference = quote.PreSettlementPrice > 0 ? quote.PreSettlementPrice : quote.LastPrice;
            var pct = reference > 0 ? (quote.LastPrice - reference) / reference : 0;
            _lbZdf.Text = pct.ToString("+0.00%;-0.00%;0.00%");
            _lbZdf.BackColor = pct >= 0 ? Theme.ChangeBack : Theme.RedBack;
        }

        var up = 0;
        var down = 0;
        foreach (var id in _session.Subscriptions)
        {
            var item = _session.GetQuote(id);
            if (item is null || item.PreSettlementPrice <= 0)
            {
                continue;
            }

            if (item.LastPrice > item.PreSettlementPrice)
            {
                up++;
            }
            else if (item.LastPrice < item.PreSettlementPrice)
            {
                down++;
            }
        }

        _lbRed.Text = up.ToString();
        _lbBlue.Text = down.ToString();
        _lbHqCount.Text = _quoteCount.ToString();
        _lbGroup.Text = $"分组: {_group}";
        _lbCjJc.Text = $"成交:{_tradeCount}";
        _lbSyMode.Text = _soundMode.ToString();
        _lbCjSyMode.Text = _offsetMode.ToString();

        UpdatePnl(active);
    }

    private void UpdatePnl(string active)
    {
        double floating = 0;
        foreach (var position in _positions.Values)
        {
            if (position.Volume <= 0)
            {
                continue;
            }

            var last = _session.GetQuote(position.InstrumentId)?.LastPrice ?? position.LastPrice;
            var multiple = Math.Max(1, _instruments.Find(position.InstrumentId)?.VolumeMultiple ?? 1);
            floating += position.Direction == PositionDirection.Long
                ? (last - position.AvgPrice) * position.Volume * multiple
                : (position.AvgPrice - last) * position.Volume * multiple;
        }

        double closed = 0;
        if (_ctx.MockTrade is { } mock)
        {
            foreach (var record in mock.GetCloseRecords())
            {
                var multiple = Math.Max(1, _instruments.Find(record.InstrumentId)?.VolumeMultiple ?? 1);
                closed += record.Pnl * multiple;
            }
        }

        var net = 0;
        foreach (var position in _positions.Values)
        {
            if (position.Volume <= 0 ||
                !string.Equals(position.InstrumentId, active, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            net += position.Direction == PositionDirection.Long ? position.Volume : -position.Volume;
        }

        _lbCcyk.Text = floating.ToString("0.00");
        _lbPcyk.Text = closed.ToString("0.00");
        _lbJt.Text = net.ToString("+0;-0;0");
        _lbPzjt.Text = (floating + closed).ToString("0");
    }

    private void OnQuoteUpdated(object? sender, Quote quote) =>
        RunOnUi(() =>
        {
            _quoteCount++;
            _lastPrices.TryGetValue(quote.InstrumentId, out var previous);
            _lastPrices[quote.InstrumentId] = quote.LastPrice;

            if (IsOn(_cbCjFlag) && previous > 0 && Math.Abs(previous - quote.LastPrice) > 1e-9)
            {
                _ladder.Flash(quote.InstrumentId, quote.LastPrice);
            }

            _ladder.Invalidate();
            UpdateInfo();
        });

    private void OnOrderUpdated(object? sender, Order order) =>
        RunOnUi(() =>
        {
            _orders[order.OrderId] = order;
            _ladder.Invalidate();
            if (order.Status == OrderStatus.Rejected)
            {
                _log.Append($"委托被拒绝：{order.OrderId} {order.StatusMessage}");
            }
        });

    private void OnTradeReceived(object? sender, Trade trade) =>
        RunOnUi(() =>
        {
            _tradeCount++;
            _ladder.Flash(trade.InstrumentId, trade.Price);
            if (_soundMode > 0)
            {
                SystemSounds.Asterisk.Play();
            }

            _log.Append(
                $"成交：{trade.InstrumentId} {DirText(trade.Direction)} " +
                $"{trade.Volume}手 @ {trade.Price:0.##}");
            UpdateInfo();
        });

    private void OnPositionUpdated(object? sender, Position position) =>
        RunOnUi(() =>
        {
            _positions[(position.InstrumentId, position.Direction)] = position;
            _ladder.Invalidate();
            UpdateInfo();
        });

    // ---------------------------------------------------------------- 行情梯

    private IReadOnlyList<Order> WorkingOrders(string instrumentId) =>
        _orders.Values
            .Where(o =>
                string.Equals(o.InstrumentId, instrumentId, StringComparison.OrdinalIgnoreCase) &&
                o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled)
            .ToArray();

    private IReadOnlyList<Position> PositionsOf(string instrumentId) =>
        _positions.Values
            .Where(p =>
                p.Volume > 0 &&
                string.Equals(p.InstrumentId, instrumentId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private void OnColumnActivated(object? sender, string instrumentId)
    {
        if (IsOn(_cbHys) && _lockedColumn is not null &&
            !string.Equals(_lockedColumn, instrumentId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Append("合约锁已开启，禁止切换列。");
            var index = _contracts.FindIndex(c =>
                string.Equals(c, _lockedColumn, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _ladder.SetActiveColumn(index);
            }

            return;
        }

        _lockedColumn = instrumentId;
        _log.Append($"当前列：{instrumentId}");
        UpdateInfo();
    }

    private async void OnLadderCellClicked(object? sender, LadderCellEventArgs e)
    {
        try
        {
            var quote = _session.GetQuote(e.InstrumentId);
            if (quote is null)
            {
                _log.Append("该合约暂无行情，无法下单。");
                return;
            }

            if (IsOn(_cbZdtLock) &&
                ((quote.UpperLimitPrice > 0 && e.Price >= quote.UpperLimitPrice) ||
                 (quote.LowerLimitPrice > 0 && e.Price <= quote.LowerLimitPrice)))
            {
                _log.Append($"涨跌停锁：{e.InstrumentId} {e.Price:0.##} 已触及涨跌停。");
                return;
            }

            if (e.HasWorkingOrder && IsOn(_cbBgds))
            {
                _log.Append($"白格单锁：{e.InstrumentId} {e.Price:0.##} 已有本账户挂单。");
                return;
            }

            if (e.HasWorkingOrder && IsOn(_cbCdLock))
            {
                _log.Append($"挂单锁定：{e.InstrumentId} 存在未成交委托，暂停下单。");
                return;
            }

            var volume = LegVolume();
            if (volume <= 0)
            {
                _log.Append("A/B 腿手数无效。");
                return;
            }

            var offset = _offsetMode switch
            {
                0 => OffsetFlag.Open,
                1 => OffsetFlag.Close,
                _ => OffsetFlag.CloseToday,
            };
            var type = IsOn(_cbMorderX) ? OrderType.Market : OrderType.Limit;
            var request = new OrderRequest
            {
                InstrumentId = e.InstrumentId,
                Direction = e.Direction,
                Offset = offset,
                Type = type,
                Price = type == OrderType.Market ? 0 : e.Price,
                Volume = volume,
            };

            var order = await _session.PlaceOrderAsync(request);
            _log.Append(
                $"行情梯下单：{e.InstrumentId} {DirText(e.Direction)} {OffsetText(offset)} " +
                $"{volume}手 @ {(type == OrderType.Market ? "市价" : e.Price.ToString("0.##"))} " +
                $"→ {order.StatusMessage}");

            if (IsOn(_cbJcQh))
            {
                await PlaceSpreadHedgeAsync(e, offset, volume);
            }
        }
        catch (Exception ex)
        {
            _log.Append($"行情梯下单失败：{ex.Message}");
            SystemSounds.Exclamation.Play();
        }
    }

    private async Task PlaceSpreadHedgeAsync(LadderCellEventArgs e, OffsetFlag offset, int volume)
    {
        var second = _txSprdId.Text.Trim();
        if (second.Length == 0 ||
            string.Equals(second, e.InstrumentId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Append("价差切换：请在 TXCntrbySprdID 填写对腿合约。");
            return;
        }

        if (!_session.Subscriptions.Contains(second, StringComparer.OrdinalIgnoreCase))
        {
            await _session.SubscribeAsync([second]);
        }

        var factor = Math.Max(1, ParseInt(_txSprdFctn.Text, 1));
        var hedgeDirection = e.Direction == OrderDirection.Buy ? OrderDirection.Sell : OrderDirection.Buy;
        await _session.PlaceOrderAsync(
            new OrderRequest
            {
                InstrumentId = second,
                Direction = hedgeDirection,
                Offset = offset,
                Type = OrderType.Market,
                Volume = Math.Max(1, volume * factor),
            });

        _log.Append($"价差对腿：{second} {DirText(hedgeDirection)} {volume * factor}手 市价。");
    }

    // ---------------------------------------------------------------- 开关与按钮

    private static bool IsOn(Label label) => label.AccessibleName == "1";

    private void SetToggle(Label label, bool on)
    {
        label.AccessibleName = on ? "1" : "0";
        label.Text = Theme.ToggleText(on, label.Tag as string ?? string.Empty);
        _log.Append($"{label.Name} {(on ? "开启" : "关闭")}");

        switch (label.Name)
        {
            case "CBShow":
                foreach (var control in _hiddenExtras)
                {
                    control.Visible = on;
                }

                break;
            case "CBHYS":
                _lockedColumn = on ? _ladder.ActiveInstrument : null;
                break;
            case "CBUpDown":
                if (on && _soundMode == 0)
                {
                    _soundMode = 1;
                }

                break;
        }

        if (label == _cbNearby || label == _cbOnlyOpen)
        {
            ApplyGroup();
        }
    }

    private void CycleGroup()
    {
        var total = Math.Max(1, (int)Math.Ceiling(_instruments.All.Count / 5.0));
        _group = _group >= total ? 1 : _group + 1;
        ApplyGroup();
        _log.Append($"切换到分组 {_group}。");
    }

    internal void SetGroup(int group)
    {
        var total = Math.Max(1, (int)Math.Ceiling(_instruments.All.Count / 5.0));
        _group = Math.Clamp(group, 1, total);
        ApplyGroup();
        _log.Append($"切换到分组 {_group}。");
    }

    private void ApplyGroup()
    {
        var all = _instruments.All.Select(i => i.InstrumentId).ToList();
        if (IsOn(_cbOnlyOpen))
        {
            var open = all
                .Where(id => _session.GetQuote(id) is { OpenInterest: > 0 } || _session.GetQuote(id) is null)
                .ToList();
            if (open.Count >= 5)
            {
                all = open;
            }
        }

        var start = (_group - 1) * 5;
        if (start >= all.Count)
        {
            _group = 1;
            start = 0;
        }

        var slice = all.Skip(start).Take(5).ToList();
        if (slice.Count == 0)
        {
            _log.Append("没有可用合约。");
            return;
        }

        _contracts.Clear();
        _contracts.AddRange(slice);
        _ladder.SetContracts(_contracts);
        UpdateInfo();
    }

    private void ReloadContracts()
    {
        ApplyGroup();
        _lbLoadFlag.Text = "已连接";
        _log.Append($"合约已重新载入：{_contracts.Count} 列。");
    }

    private void ShowRanking(int mode)
    {
        if (mode == 0)
        {
            _rankingMode = 0;
            ApplyGroup();
            return;
        }

        var ranked = _instruments.All
            .Select(instrument => (Id: instrument.InstrumentId, Quote: _session.GetQuote(instrument.InstrumentId)))
            .Where(pair => pair.Quote is not null && pair.Quote.PreSettlementPrice > 0)
            .OrderByDescending(pair =>
                (pair.Quote!.LastPrice - pair.Quote.PreSettlementPrice) / pair.Quote.PreSettlementPrice)
            .Select(pair => pair.Id)
            .ToList();

        if (ranked.Count < 5)
        {
            _log.Append("行情不足，无法生成涨跌幅排行。");
            return;
        }

        _rankingMode = mode;
        _contracts.Clear();
        _contracts.AddRange(mode == 1 ? ranked.Take(5) : ranked.TakeLast(5).Reverse());
        _ladder.SetContracts(_contracts);
        _log.Append(mode == 1 ? "行情梯切换为涨幅榜。" : "行情梯切换为跌幅榜。");
        UpdateInfo();
    }

    private void ChangeDepth(int delta)
    {
        var depth = Math.Clamp(_ladder.Depth + delta, 3, 60);
        _ladder.Depth = depth;
        _txPriceTick.Text = depth.ToString();
        _ladder.Invalidate();
        _log.Append($"行情梯深度：{depth} 档。");
    }

    private void ApplyDepth()
    {
        var depth = Math.Clamp(ParseInt(_txPriceTick.Text, _ladder.Depth), 3, 60);
        _ladder.Depth = depth;
        _txPriceTick.Text = depth.ToString();
        _ladder.Invalidate();
    }

    private void CycleStepTicks()
    {
        _ladder.StepTicks = _ladder.StepTicks switch
        {
            1 => 2,
            2 => 5,
            5 => 10,
            _ => 1,
        };
        _txSprdPt.Text = _ladder.StepTicks.ToString();
        _ladder.Invalidate();
        _log.Append($"价格步长：{_ladder.StepTicks} 跳。");
    }

    private void CycleOffsetMode()
    {
        _offsetMode = (_offsetMode + 1) % 3;
        _lbCjSyMode.Text = _offsetMode.ToString();
        _log.Append(_offsetMode switch
        {
            0 => "开平模式：开仓。",
            1 => "开平模式：平仓。",
            _ => "开平模式：平今。",
        });
    }

    private void CycleSoundMode()
    {
        _soundMode = (_soundMode + 1) % 3;
        _lbSyMode.Text = _soundMode.ToString();
        _log.Append(_soundMode switch
        {
            0 => "声音：关闭。",
            1 => "声音：成交提示。",
            _ => "声音：全部提示。",
        });
    }

    private int LegVolume()
    {
        var text = _rboA.Checked ? _valLeft.Text : _valRight.Text;
        return Math.Clamp(ParseInt(text, 1), 1, 999);
    }

    private void ResetScroll()
    {
        _ladder.ResetScroll();
        _log.Append("行情梯回到最新价。");
    }

    // ---------------------------------------------------------------- 批量操作

    internal async Task CancelAllOrdersAsync()
    {
        var working = _orders.Values
            .Where(o => o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled)
            .ToArray();

        if (working.Length == 0)
        {
            _log.Append("当前没有可撤委托。");
            return;
        }

        foreach (var order in working)
        {
            try
            {
                await _session.CancelOrderAsync(order.OrderId);
            }
            catch (Exception ex)
            {
                _log.Append($"撤单 {order.OrderId} 失败：{ex.Message}");
            }
        }

        _log.Append($"已撤 {working.Length} 笔委托。");
    }

    internal async Task CloseAllPositionsAsync()
    {
        var positions = _positions.Values.Where(p => p.Volume > 0).ToArray();
        if (positions.Length == 0)
        {
            _log.Append("当前没有持仓。");
            return;
        }

        foreach (var position in positions)
        {
            await ClosePositionAsync(position);
        }

        _log.Append($"已提交 {positions.Length} 笔全平委托。");
    }

    internal async Task CloseGroupPositionsAsync()
    {
        var positions = _positions.Values
            .Where(p => p.Volume > 0 &&
                        _contracts.Contains(p.InstrumentId, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (var position in positions)
        {
            await ClosePositionAsync(position);
        }

        _log.Append($"已提交 {positions.Length} 笔关组委托。");
    }

    private async Task ClosePositionAsync(Position position)
    {
        try
        {
            await _session.PlaceOrderAsync(
                new OrderRequest
                {
                    InstrumentId = position.InstrumentId,
                    Direction = position.Direction == PositionDirection.Long
                        ? OrderDirection.Sell
                        : OrderDirection.Buy,
                    Offset = OffsetFlag.Close,
                    Type = OrderType.Market,
                    Volume = position.Volume,
                });
        }
        catch (Exception ex)
        {
            _log.Append($"平仓 {position.InstrumentId} 失败：{ex.Message}");
        }
    }

    internal void ActivateInstrument(string instrumentId)
    {
        var index = _contracts.FindIndex(c =>
            string.Equals(c, instrumentId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            if (_contracts.Count < PriceLadder.MaxColumns)
            {
                _contracts.Add(instrumentId);
            }
            else
            {
                _contracts[_ladder.ActiveColumn] = instrumentId;
            }

            _ladder.SetContracts(_contracts);
            index = _contracts.FindIndex(c =>
                string.Equals(c, instrumentId, StringComparison.OrdinalIgnoreCase));
        }

        if (index >= 0)
        {
            _ladder.SetActiveColumn(index);
        }

        Activate();
    }

    internal void SetSpreadMode(bool on)
    {
        if (IsOn(_cbJcQh) != on)
        {
            SetToggle(_cbJcQh, on);
        }
    }

    internal void ToggleSpreadMode() => SetSpreadMode(!IsOn(_cbJcQh));

    internal void AlignWindow()
    {
        var area = Screen.FromControl(this).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width), Math.Max(area.Top, area.Bottom - Height));
        _log.Append("主窗体已对齐到屏幕右下角。");
    }

    internal void RefreshVolumeList() => _ladder.Invalidate();

    internal void ShowCloseRecords() => OpenCloseRecords();

    internal void SetWorkingOrdersVisible(bool visible)
    {
        _ladder.ShowWorkingOrders = visible;
        _ladder.Invalidate();
    }

    internal void ToggleTradeFlag() => SetToggle(_cbCjFlag, !IsOn(_cbCjFlag));

    internal void CyclePriceStep() => CycleStepTicks();

    internal async Task CancelGroupOrdersAsync()
    {
        var orders = _orders.Values
            .Where(o => (o.Status is OrderStatus.Pending or OrderStatus.PartiallyFilled) &&
                        _contracts.Contains(o.InstrumentId, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (var order in orders)
        {
            try
            {
                await _session.CancelOrderAsync(order.OrderId);
            }
            catch (Exception ex)
            {
                _log.Append($"撤组 {order.OrderId} 失败：{ex.Message}");
            }
        }

        _log.Append($"已撤当前分组 {orders.Length} 笔委托。");
    }

    internal (string Account, string Floating, string Closed, string Total) PnlSummary()
    {
        var active = _ladder.ActiveInstrument ?? _contracts.FirstOrDefault() ?? string.Empty;
        UpdatePnl(active);
        return (_ctx.Session.Account?.DisplayName ?? "未登录", _lbCcyk.Text, _lbPcyk.Text, _lbPzjt.Text);
    }

    internal IEnumerable<Position> OpenPositions => _positions.Values.Where(p => p.Volume > 0);

    internal void SelectLeg(bool legA)
    {
        if (legA)
        {
            _rboA.Checked = true;
        }
        else
        {
            _rboB.Checked = true;
        }

        UpdateInfo();
    }

    // ---------------------------------------------------------------- 子窗口

    private void OpenConsole()
    {
        if (_console is { IsDisposed: false })
        {
            _console.Activate();
            return;
        }

        _console = new ControlCenterForm(this, _ctx);
        _console.FormClosed += (_, _) => _console = null;
        _console.Show(this);
    }

    private void OpenCloseRecords()
    {
        if (_records is { IsDisposed: false })
        {
            _records.Activate();
            return;
        }

        _records = new CloseRecordForm(_ctx);
        _records.FormClosed += (_, _) => _records = null;
        _records.Show(this);
    }

    private void OpenUserEditor()
    {
        using var editor = new UserEditForm(_ctx.UsersPath, _ctx.Accounts, _log);
        editor.ShowDialog(this);
    }

    private void OpenPointWindow()
    {
        if (_point is { IsDisposed: false })
        {
            _point.Activate();
            return;
        }

        _point = new PointWindow(this, _ctx);
        _point.FormClosed += (_, _) => _point = null;
        _point.Show(this);
    }

    private void OpenInstruments()
    {
        using var window = new InstrumentsForm(this, _ctx);
        window.ShowDialog(this);
    }

    private void OpenConfig()
    {
        using var window = new ConfigForm(_ctx, _log);
        window.ShowDialog(this);
    }

    private void OpenMonitor()
    {
        using var window = new MonitorForm(_ctx);
        window.ShowDialog(this);
    }

    private void OpenAlertWindow()
    {
        using var window = new AlertForm(_ctx);
        window.ShowDialog(this);
    }

    private void OpenSoundRecords()
    {
        if (_soundRecords is { IsDisposed: false })
        {
            _soundRecords.Activate();
            return;
        }

        _soundRecords = new RecordListForm("涨跌记录", 205, 564, "LXZDSY", BuildChangeRecords);
        _soundRecords.FormClosed += (_, _) => _soundRecords = null;
        _soundRecords.Show(this);
    }

    private void OpenSpreadRecords()
    {
        if (_spreadRecords is { IsDisposed: false })
        {
            _spreadRecords.Activate();
            return;
        }

        _spreadRecords = new RecordListForm("价差记录", 334, 564, "LXCJJL", BuildSpreadRecords);
        _spreadRecords.FormClosed += (_, _) => _spreadRecords = null;
        _spreadRecords.Show(this);
    }

    private void OpenMinuteBars()
    {
        if (_minuteBars is { IsDisposed: false })
        {
            _minuteBars.Activate();
            return;
        }

        _minuteBars = new MinuteBarsForm(_session);
        _minuteBars.FormClosed += (_, _) => _minuteBars = null;
        _minuteBars.Show(this);
    }

    private IReadOnlyList<RecordEntry> BuildChangeRecords()
    {
        var now = DateTime.Now.ToString("HH:mm:ss");
        var entries = _session.Subscriptions
            .Select(id => (Id: id, Quote: _session.GetQuote(id)))
            .Where(pair => pair.Quote is { PreSettlementPrice: > 0 })
            .Select(pair =>
            {
                var quote = pair.Quote!;
                var percent = (quote.LastPrice - quote.PreSettlementPrice) / quote.PreSettlementPrice * 100;
                return (pair.Id, Price: quote.LastPrice, Percent: percent);
            })
            .OrderByDescending(pair => Math.Abs(pair.Percent))
            .Take(40)
            .Select(pair => new RecordEntry(
                $"{now} {pair.Id} {pair.Price:0.###} {pair.Percent:+0.00;-0.00;0.00}%",
                pair.Percent >= 0 ? Color.Firebrick : Color.SeaGreen,
                Color.White))
            .ToList();

        return entries;
    }

    private IReadOnlyList<RecordEntry> BuildSpreadRecords()
    {
        var lines = _log.Snapshot()
            .Where(line => line.Contains("价差", StringComparison.Ordinal))
            .TakeLast(200)
            .Reverse()
            .Select(line => new RecordEntry(
                line,
                line.Contains("对腿", StringComparison.Ordinal) ? Color.Teal : Color.Black,
                Color.White))
            .ToList();

        return lines;
    }

    // ---------------------------------------------------------------- 键盘与窗口

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                WindowState = FormWindowState.Minimized;
                break;
            case Keys.F1:
                OpenConsole();
                break;
            case Keys.F2:
                OpenCloseRecords();
                break;
            case Keys.F3:
                OpenUserEditor();
                break;
            case Keys.F4:
                SetToggle(_cbShow, !IsOn(_cbShow));
                break;
            case Keys.F5:
                ReloadContracts();
                break;
            case Keys.F6:
                CycleGroup();
                break;
            case Keys.F7:
                AlignWindow();
                break;
            case Keys.F8:
                _ = CancelAllOrdersAsync();
                break;
            case Keys.F9:
                _ = CloseAllPositionsAsync();
                break;
            case Keys.Up:
                _ladder.ScrollRows(1);
                break;
            case Keys.Down:
                _ladder.ScrollRows(-1);
                break;
            case Keys.Left:
                _ladder.SetActiveColumn(Math.Max(0, _ladder.ActiveColumn - 1));
                break;
            case Keys.Right:
                _ladder.SetActiveColumn(Math.Min(_contracts.Count - 1, _ladder.ActiveColumn + 1));
                break;
            case Keys.Add:
            case Keys.Oemplus:
                ChangeDepth(1);
                break;
            case Keys.Subtract:
            case Keys.OemMinus:
                ChangeDepth(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _ladder.ScrollRows(-Math.Sign(e.Delta) * Math.Max(1, _ladder.WheelRows));
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

        _clock.Dispose();
        _ladder.Dispose();

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

    private void PositionWindow()
    {
        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        var area = Screen.FromControl(this).WorkingArea;
        var width = Math.Max(Width, ClientSize.Width + (Width - ClientSize.Width));
        Location = new Point(
            Math.Max(area.Left, area.Right - width),
            Math.Max(area.Top, area.Top));
    }

    // ---------------------------------------------------------------- 工具

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

    private static void NumericOnly(TextBox box)
    {
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != '-')
            {
                e.Handled = true;
            }
        };
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var result) ? result : fallback;

    private static string DirText(OrderDirection direction) =>
        direction == OrderDirection.Buy ? "买" : "卖";

    private static string OffsetText(OffsetFlag offset) => offset switch
    {
        OffsetFlag.Open => "开仓",
        OffsetFlag.Close => "平仓",
        OffsetFlag.CloseToday => "平今",
        _ => "平昨",
    };
}
