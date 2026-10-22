using System.Diagnostics;
using System.Net.Sockets;
using Replica.Core.Abstractions;
using Replica.Core.Mock;
using Replica.Core.Models;
using Replica.Core.Services;
using Replica.Ctp;

namespace Replica.Desktop;

/// <summary>
/// 登录窗体，严格按原软件 TForm1 的布局重建：
/// 左上行情服务、左下用户列表、中下交易服务、右侧 SYSLOG，
/// 底部密码、登陆、备用、独立、刷新合约与设置按钮。
/// 不包含授权码、MAC 绑定与云端认证。
/// </summary>
public sealed class LoginForm : Form
{
    private sealed class ServerItem(string name, string address)
    {
        public string Name { get; } = name;

        public string Address { get; } = address;

        public int? Latency { get; set; }

        public override string ToString() => $"{Name}  {Address}";
    }

    private readonly LogHub _log = new();
    private readonly ListBox _userList = new();
    private readonly ListView _marketList = new();
    private readonly ListView _tradeList = new();
    private readonly TextBox _password = new();
    private readonly Button _loginButton = new();
    private readonly CheckBox _backupCheck = new();
    private readonly CheckBox _independentCheck = new();
    private readonly CheckBox _refreshCheck = new();
    private readonly Label _currentMarket = new();
    private readonly TextBox _syslog = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _latencyTimer = new() { Interval = 10_000 };

    private readonly string _usersPath;
    private readonly string _configPath;
    private readonly DesktopConfig _config;
    private IReadOnlyList<Account> _accounts = [];
    private InstrumentRepository _instruments = InstrumentRepository.Default;
    private Account? _selectedAccount;
    private TradingSession? _session;
    private bool _handedOff;
    private bool _probing;

    public LoginForm()
    {
        _usersPath = Path.Combine(AppContext.BaseDirectory, "Users.xml");
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.ini");
        _config = DesktopConfig.Load(_configPath);
        _accounts = UserRepository.LoadOrSeed(_usersPath);
        _instruments = LoadInstruments();

        Text = "YY交易";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1017), Theme.S(644));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Theme.MenuBack;
        Font = Theme.F(36, false, "Consolas");
        DoubleBuffered = true;

        BuildLayout();
        PopulateServers();
        PopulateUsers();
        _log.Appended += (_, line) => _syslog.AppendText(line + Environment.NewLine);

        var menu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("保存退出");
        exitItem.Click += (_, _) => SaveAndExit();
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "YY交易",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };

        _latencyTimer.Tick += async (_, _) => await ProbeLatencyAsync();
        _latencyTimer.Start();

        _log.Append("登录窗口已启动，选择行情与交易服务后登陆。");
    }

    private void BuildLayout()
    {
        _marketList.Left = 0;
        _marketList.Top = 0;
        _marketList.Width = Theme.S(585);
        _marketList.Height = Theme.S(278);
        _marketList.View = View.Details;
        _marketList.FullRowSelect = true;
        _marketList.MultiSelect = false;
        _marketList.HideSelection = false;
        _marketList.Font = Theme.F(24);
        _marketList.Columns.Add("行情名称", Theme.S(120));
        _marketList.Columns.Add("行情地址", Theme.S(350));
        _marketList.Columns.Add("延迟", Theme.S(86));
        _marketList.SelectedIndexChanged += (_, _) =>
        {
            if (_marketList.SelectedItems.Count > 0 &&
                _marketList.SelectedItems[0].Tag is ServerItem server)
            {
                _currentMarket.Text = $"当前行情服务: {server.Name}";
            }
        };

        _tradeList.Left = Theme.S(143);
        _tradeList.Top = Theme.S(278);
        _tradeList.Width = Theme.S(442);
        _tradeList.Height = Theme.S(285);
        _tradeList.View = View.Details;
        _tradeList.FullRowSelect = true;
        _tradeList.MultiSelect = false;
        _tradeList.HideSelection = false;
        _tradeList.Font = Theme.F(24);
        _tradeList.Columns.Add("交易地址", Theme.S(330));
        _tradeList.Columns.Add("延迟", Theme.S(101));

        _userList.Left = 0;
        _userList.Top = Theme.S(278);
        _userList.Width = Theme.S(141);
        _userList.Height = Theme.S(285);
        _userList.Font = Theme.F(24);
        _userList.IntegralHeight = false;
        _userList.SelectedIndexChanged += (_, _) => OnUserSelected();
        _userList.DoubleClick += (_, _) => OpenUserEditor();

        _syslog.Left = Theme.S(587);
        _syslog.Top = 0;
        _syslog.Width = Theme.S(429);
        _syslog.Height = Theme.S(644);
        _syslog.Multiline = true;
        _syslog.ReadOnly = true;
        _syslog.ScrollBars = ScrollBars.Vertical;
        _syslog.Font = Theme.F(24);
        _syslog.BackColor = Color.White;
        _syslog.BorderStyle = BorderStyle.FixedSingle;

        Theme.MakeLabel(
            this,
            "LBHQselect",
            Theme.S(45),
            Theme.S(570),
            Theme.S(156),
            Theme.S(24),
            "当前行情服务:",
            Theme.F(24),
            Theme.Delphi(6645019));

        _currentMarket.SetBounds(Theme.S(45), Theme.S(587), Theme.S(300), Theme.S(18));
        _currentMarket.AutoSize = false;
        _currentMarket.Text = string.Empty;
        _currentMarket.Font = Theme.F(20);
        _currentMarket.ForeColor = Color.DimGray;
        Controls.Add(_currentMarket);

        var passwordLabel = Theme.MakeLabel(
            this,
            "Label1",
            0,
            Theme.S(611),
            Theme.S(48),
            Theme.S(24),
            "密: ",
            Theme.F(24),
            Color.Red);
        passwordLabel.BackColor = Theme.MenuBack;

        var options = Theme.MakeButton(
            this,
            "BtOptions",
            0,
            Theme.S(560),
            Theme.S(35),
            Theme.S(36),
            "⚙",
            Theme.F(32),
            Color.Black,
            Theme.MenuBack);
        options.Click += (_, _) => OpenUserEditor();

        _password.Left = Theme.S(45);
        _password.Top = Theme.S(606);
        _password.Width = Theme.S(161);
        _password.Height = Theme.S(32);
        _password.Font = Theme.F(24);
        _password.ForeColor = Color.Red;
        _password.PasswordChar = '*';
        _password.BorderStyle = BorderStyle.FixedSingle;
        _password.KeyPress += (_, e) =>
        {
            if (e.KeyChar == '\r')
            {
                _ = LoginAsync();
            }
        };

        _loginButton.Left = Theme.S(510);
        _loginButton.Top = Theme.S(605);
        _loginButton.Width = Theme.S(73);
        _loginButton.Height = Theme.S(36);
        _loginButton.Text = "登陆";
        _loginButton.Font = Theme.F(24);
        _loginButton.ForeColor = Theme.Delphi(16233578);
        _loginButton.Enabled = false;
        _loginButton.Click += (_, _) => _ = LoginAsync();

        _backupCheck.Left = Theme.S(434);
        _backupCheck.Top = Theme.S(601);
        _backupCheck.Width = Theme.S(80);
        _backupCheck.Height = Theme.S(42);
        _backupCheck.Text = "备用";
        _backupCheck.Font = Theme.F(24);
        _backupCheck.ForeColor = Color.Green;
        _backupCheck.BackColor = Theme.MenuBack;
        _backupCheck.Checked = _config.GetBool("Login", "Backup", false);

        _independentCheck.Left = Theme.S(352);
        _independentCheck.Top = Theme.S(601);
        _independentCheck.Width = Theme.S(80);
        _independentCheck.Height = Theme.S(42);
        _independentCheck.Text = "独立";
        _independentCheck.Font = Theme.F(24);
        _independentCheck.ForeColor = Color.Maroon;
        _independentCheck.BackColor = Theme.MenuBack;
        _independentCheck.Checked = _config.GetBool("Login", "Independent", true);

        _refreshCheck.Left = Theme.S(216);
        _refreshCheck.Top = Theme.S(601);
        _refreshCheck.Width = Theme.S(133);
        _refreshCheck.Height = Theme.S(42);
        _refreshCheck.Text = "刷新合约";
        _refreshCheck.Font = Theme.F(24);
        _refreshCheck.ForeColor = Theme.Delphi(16711808);
        _refreshCheck.BackColor = Theme.MenuBack;
        _refreshCheck.Checked = _config.GetBool("Login", "RefreshInstruments", false);
        _refreshCheck.CheckedChanged += (_, _) =>
        {
            if (_refreshCheck.Checked)
            {
                RefreshInstruments();
            }
        };

        Controls.AddRange(
        [
            _marketList,
            _tradeList,
            _userList,
            _syslog,
            _password,
            _loginButton,
            _backupCheck,
            _independentCheck,
            _refreshCheck,
        ]);

        AcceptButton = _loginButton;
    }

    private void PopulateServers()
    {
        var marketServers = new[]
        {
            new ServerItem("本地模拟行情", "mock://market"),
            new ServerItem("SimNow 7x24 行情", "tcp://180.168.146.187:10211"),
            new ServerItem("SimNow 电信 行情", "tcp://180.168.146.187:10213"),
            new ServerItem("SimNow 移动 行情", "tcp://218.202.237.33:10213"),
        };

        var tradeServers = new[]
        {
            new ServerItem("本地模拟交易", "mock://trade"),
            new ServerItem("SimNow 7x24 交易", "tcp://180.168.146.187:10130"),
            new ServerItem("SimNow 电信 交易", "tcp://180.168.146.187:10131"),
            new ServerItem("SimNow 移动 交易", "tcp://218.202.237.33:10131"),
        };

        FillServerList(_marketList, marketServers);
        FillServerList(_tradeList, tradeServers);

        var marketIndex = Math.Clamp(_config.GetInt("Login", "MarketServer", 0), 0, marketServers.Length - 1);
        var tradeIndex = Math.Clamp(_config.GetInt("Login", "TradeServer", 0), 0, tradeServers.Length - 1);
        if (_marketList.Items.Count > marketIndex)
        {
            _marketList.Items[marketIndex].Selected = true;
        }

        if (_tradeList.Items.Count > tradeIndex)
        {
            _tradeList.Items[tradeIndex].Selected = true;
        }
    }

    private static void FillServerList(ListView list, IReadOnlyList<ServerItem> servers)
    {
        list.Items.Clear();
        foreach (var server in servers)
        {
            var item = new ListViewItem(server.Name) { Tag = server };
            item.SubItems.Add(server.Address);
            item.SubItems.Add("—");
            list.Items.Add(item);
        }
    }

    private void PopulateUsers()
    {
        _userList.Items.Clear();
        foreach (var account in _accounts)
        {
            _userList.Items.Add(account.DisplayName);
        }

        var lastName = _config.Get("Login", "UserName", string.Empty);
        var index = _accounts
            .Select((account, i) => (account, i))
            .FirstOrDefault(pair => pair.account.DisplayName == lastName)
            .i;

        if (_userList.Items.Count > 0)
        {
            _userList.SelectedIndex = index >= 0 && index < _userList.Items.Count ? index : 0;
        }
    }

    private void OnUserSelected()
    {
        var index = _userList.SelectedIndex;
        _selectedAccount = index >= 0 && index < _accounts.Count ? _accounts[index] : null;
        _loginButton.Enabled = _selectedAccount is not null;
        if (_selectedAccount is not null)
        {
            var account = _selectedAccount;
            _log.Append(
                $"选择用户 {account.DisplayName}：{account.BrokerId}/{account.UserId}，行情 {account.MarketFront}");
            if (string.IsNullOrWhiteSpace(_password.Text) &&
                account.MarketFront.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            {
                _password.Text = "mock";
            }
        }
    }

    private async Task ProbeLatencyAsync()
    {
        if (_probing)
        {
            return;
        }

        _probing = true;
        try
        {
            foreach (var list in new[] { _marketList, _tradeList })
            {
                foreach (ListViewItem item in list.Items)
                {
                    if (item.Tag is not ServerItem server)
                    {
                        continue;
                    }

                    server.Latency = await MeasureAsync(server.Address);
                    if (!IsDisposed && item.SubItems.Count > 2)
                    {
                        item.SubItems[2].Text = server.Latency is { } ms ? $"{ms} ms" : "—";
                    }
                }
            }
        }
        finally
        {
            _probing = false;
        }
    }

    private static async Task<int?> MeasureAsync(string address)
    {
        if (address.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        string host;
        int port;
        try
        {
            var front = CtpFrontAddress.Parse(address, "行情地址");
            host = front.Host;
            port = front.Port;
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            var watch = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, cts.Token);
            watch.Stop();
            return (int)Math.Max(1, watch.ElapsedMilliseconds);
        }
        catch
        {
            return null;
        }
    }

    private async Task LoginAsync()
    {
        if (_selectedAccount is null)
        {
            return;
        }

        if (_marketList.SelectedItems.Count == 0 || _tradeList.SelectedItems.Count == 0)
        {
            _log.Append("请先选择行情服务与交易服务。");
            return;
        }

        var marketServer = (ServerItem)_marketList.SelectedItems[0].Tag!;
        var tradeServer = (ServerItem)_tradeList.SelectedItems[0].Tag!;
        var password = _password.Text;
        var isMock = marketServer.Address.StartsWith("mock://", StringComparison.OrdinalIgnoreCase) &&
                     tradeServer.Address.StartsWith("mock://", StringComparison.OrdinalIgnoreCase);

        if (!isMock && password.Length == 0)
        {
            _log.Append("实盘登录需要输入密码。");
            _password.Focus();
            return;
        }

        SetLoginEnabled(false);
        TradingSession? created = null;
        try
        {
            var account = _selectedAccount with
            {
                Password = password,
                MarketFront = marketServer.Address,
                TradeFront = tradeServer.Address,
            };

            _log.Append($"开始登录：{account.DisplayName} / {account.BrokerId}:{account.UserId}");
            _log.Append($"行情 {marketServer.Address}，交易 {tradeServer.Address}");

            (IMarketDataClient market, ITradeClient trade, MockTradeClient? mock) =
                CreateClients(marketServer.Address, tradeServer.Address, isMock);

            var session = new TradingSession(market, trade);
            created = session;
            session.Log += (_, message) => _log.Append(message);

            if (mock is not null)
            {
                session.QuoteUpdated += (_, quote) => mock.OnQuote(quote);
            }

            try
            {
                await session.ConnectAsync(account);
            }
            catch when (_backupCheck.Checked && !isMock)
            {
                _log.Append("主服务连接失败，尝试备用服务。");
                var backupMarket = FindBackup(_marketList, marketServer);
                var backupTrade = FindBackup(_tradeList, tradeServer);
                if (backupMarket is null || backupTrade is null)
                {
                    throw;
                }

                await session.DisposeAsync();
                (market, trade, mock) = CreateClients(backupMarket.Address, backupTrade.Address, false);
                session = new TradingSession(market, trade);
                created = session;
                session.Log += (_, message) => _log.Append(message);
                account = account with
                {
                    MarketFront = backupMarket.Address,
                    TradeFront = backupTrade.Address,
                };
                await session.ConnectAsync(account);
            }

            var ids = _instruments.All.Select(i => i.InstrumentId).ToArray();
            await session.SubscribeAsync(ids);
            _log.Append($"合约载入完成：{ids.Length} 个。");

            _session = session;
            SaveLoginConfig(marketServer, tradeServer, account);

            var context = new ReplicaContext
            {
                Session = session,
                Instruments = _instruments,
                Accounts = _accounts,
                Log = _log,
                UsersPath = _usersPath,
                ConfigPath = _configPath,
                IsMock = isMock,
                MockTrade = mock,
            };

            var main = new MainForm(context);
            main.FormClosed += (_, _) =>
            {
                _handedOff = true;
                Close();
            };

            Hide();
            main.Show();
        }
        catch (Exception ex)
        {
            if (created is not null && !ReferenceEquals(created, _session))
            {
                try
                {
                    await created.DisposeAsync();
                }
                catch
                {
                    // 清理失败不覆盖原始错误。
                }
            }

            _log.Append($"登录失败：{ex.Message}");
            SetLoginEnabled(true);
            MessageBox.Show(
                this,
                ex.Message,
                "登陆失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static (IMarketDataClient Market, ITradeClient Trade, MockTradeClient? Mock) CreateClients(
        string marketFront,
        string tradeFront,
        bool isMock)
    {
        if (isMock)
        {
            IMarketDataClient market = new MockMarketDataClient(InstrumentRepository.Default);
            var provider = new Func<string, Quote?>(_ => null);
            var mockTrade = new MockTradeClient(id => provider(id));
            return (market, mockTrade, mockTrade);
        }

        return (new CtpMarketDataClient(), new CtpTradeClient(), null);
    }

    private static ServerItem? FindBackup(ListView list, ServerItem current)
    {
        var items = list.Items.Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<ServerItem>()
            .ToList();
        var index = items.IndexOf(current);
        return index >= 0 && index + 1 < items.Count ? items[index + 1] : null;
    }

    private void SetLoginEnabled(bool enabled)
    {
        _loginButton.Enabled = enabled && _selectedAccount is not null;
        _userList.Enabled = enabled;
        _marketList.Enabled = enabled;
        _tradeList.Enabled = enabled;
        _password.Enabled = enabled;
    }

    private void OpenUserEditor()
    {
        using var editor = new UserEditForm(_usersPath, _accounts, _log);
        editor.ShowDialog(this);
        if (!editor.Saved)
        {
            return;
        }

        _accounts = editor.Accounts;
        PopulateUsers();
        _log.Append("账号配置已更新。");
    }

    private void RefreshInstruments()
    {
        _instruments = LoadInstruments();
        _log.Append($"合约刷新完成，共 {_instruments.All.Count} 个合约。");
    }

    private static InstrumentRepository LoadInstruments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Instruments.xml");
        if (!File.Exists(path))
        {
            return InstrumentRepository.Default;
        }

        try
        {
            var repository = InstrumentRepository.Load(path);
            return repository.All.Count > 0 ? repository : InstrumentRepository.Default;
        }
        catch
        {
            return InstrumentRepository.Default;
        }
    }

    private void SaveLoginConfig(ServerItem market, ServerItem trade, Account account)
    {
        _config.Set("Login", "MarketServer", _marketList.SelectedIndices.Count > 0 ? _marketList.SelectedIndices[0] : 0);
        _config.Set("Login", "TradeServer", _tradeList.SelectedIndices.Count > 0 ? _tradeList.SelectedIndices[0] : 0);
        _config.Set("Login", "UserName", account.DisplayName);
        _config.Set("Login", "Backup", _backupCheck.Checked);
        _config.Set("Login", "Independent", _independentCheck.Checked);
        _config.Set("Login", "RefreshInstruments", _refreshCheck.Checked);
        try
        {
            _config.Save(_configPath);
        }
        catch (Exception ex)
        {
            _log.Append($"保存配置失败：{ex.Message}");
        }
    }

    private void SaveAndExit()
    {
        try
        {
            _config.Save(_configPath);
        }
        catch
        {
            // 退出时忽略配置保存异常。
        }

        _tray.Visible = false;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _latencyTimer.Stop();
        _latencyTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();

        if (!_handedOff && _session is not null)
        {
            try
            {
                _session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 退出时忽略清理异常。
            }
        }

        base.OnFormClosed(e);
    }
}
