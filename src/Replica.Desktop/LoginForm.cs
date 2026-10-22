using Replica.Core.Abstractions;
using Replica.Ctp;
using Replica.Core.Mock;
using Replica.Core.Models;
using Replica.Core.Services;

namespace Replica.Desktop;

/// <summary>
/// 登录窗体，对应原软件 TForm1。
/// 只保留期货账户与前置地址，不包含授权码、MAC、云端认证。
/// </summary>
public sealed class LoginForm : Form
{
    private readonly TextBox _tradeFront = new() { Text = "mock://trade" };
    private readonly TextBox _marketFront = new() { Text = "mock://market" };
    private readonly TextBox _brokerId = new() { Text = "9999" };
    private readonly TextBox _userId = new() { Text = "simnow_demo" };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _useMock = new()
    {
        Text = "使用模拟行情与模拟撮合",
        Checked = true,
        AutoSize = true,
    };

    private readonly Button _loginButton = new() { Text = "登录", Width = 120, Height = 34 };
    private TradingSession? _session;
    private bool _handedOff;

    public LoginForm()
    {
        Text = "登录 · 期货交易终端（复刻版）";
        ClientSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(24),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "交易前置", _tradeFront);
        AddRow(layout, 1, "行情前置", _marketFront);
        AddRow(layout, 2, "经纪商代码", _brokerId);
        AddRow(layout, 3, "资金账号", _userId);
        AddRow(layout, 4, "密码", _password);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(_useMock, 1, 5);

        var hint = new Label
        {
            Text = "复刻版仅保留期货账户登录，不包含厂商授权码与 MAC 校验。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(320, 0),
            Margin = new Padding(0, 8, 0, 8),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(hint, 1, 6);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttons.Controls.Add(_loginButton);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(buttons, 1, 7);

        Controls.Add(layout);
        _loginButton.Click += OnLoginClick;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(
            new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            },
            0,
            row);

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(control, 1, row);
    }

    private async void OnLoginClick(object? sender, EventArgs e)
    {
        _loginButton.Enabled = false;
        TradingSession? createdSession = null;
        try
        {
            var account = new Account
            {
                BrokerId = _brokerId.Text.Trim(),
                UserId = _userId.Text.Trim(),
                Password = _password.Text,
                DisplayName = _userId.Text.Trim(),
                TradeFront = _tradeFront.Text.Trim(),
                MarketFront = _marketFront.Text.Trim(),
            };

            if (account.BrokerId.Length == 0 || account.UserId.Length == 0)
            {
                throw new InvalidOperationException("经纪商代码和资金账号不能为空");
            }

            var instruments = InstrumentRepository.Default;
            TradingSession? sessionRef = null;

            IMarketDataClient market;
            ITradeClient trade;
            if (_useMock.Checked)
            {
                market = new MockMarketDataClient(instruments);
                trade = new MockTradeClient(id => sessionRef?.GetQuote(id));
            }
            else
            {
                market = new CtpMarketDataClient();
                trade = new CtpTradeClient();
            }

            var tradingSession = new TradingSession(market, trade);
            createdSession = tradingSession;
            sessionRef = tradingSession;

            await tradingSession.ConnectAsync(account);
            await tradingSession.SubscribeAsync(["rb2601", "IF2601", "au2612"]);
            _session = tradingSession;

            var mainForm = new MainForm(tradingSession, instruments);
            mainForm.FormClosed += (_, _) =>
            {
                _handedOff = true;
                Close();
            };

            Hide();
            mainForm.Show();
        }
        catch (Exception ex)
        {
            if (createdSession is not null && !ReferenceEquals(createdSession, _session))
            {
                try
                {
                    await createdSession.DisposeAsync();
                }
                catch
                {
                    // 清理失败不覆盖原始错误。
                }
            }

            _loginButton.Enabled = true;
            MessageBox.Show(
                this,
                ex.Message,
                "登录失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
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
