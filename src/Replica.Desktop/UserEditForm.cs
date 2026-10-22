using Replica.Core.Models;
using Replica.Core.Services;

namespace Replica.Desktop;

/// <summary>
/// 账号编辑器，按原软件 TUserEdit 的 1323×673 布局重建。
/// 保留名称、期货账号、BrokerID、AppID、行情/交易地址，
/// 明确去掉授权码（TXshouquan）与任何 MAC 绑定字段。
/// </summary>
internal sealed class UserEditForm : Form
{
    private readonly string _usersPath;
    private readonly LogHub _log;
    private readonly List<Account> _accounts;

    private readonly DataGridView _grid = new();
    private readonly TextBox _name = new();
    private readonly TextBox _userId = new();
    private readonly TextBox _brokerId = new();
    private readonly TextBox _appId = new();
    private readonly TextBox _marketFront = new();
    private readonly TextBox _tradeFront = new();
    private readonly ListBox _leftUsers = new();
    private readonly ListBox _rightUsers = new();
    private readonly TextBox _parse = new();

    private int _editingIndex = -1;

    public UserEditForm(string usersPath, IReadOnlyList<Account> accounts, LogHub log)
    {
        _usersPath = usersPath;
        _log = log;
        _accounts = accounts.ToList();

        Text = "账号编辑器";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(1323), Theme.S(673));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        Font = Theme.F(24);

        BuildLayout();
        PopulateGrid();
        PopulateLists();
    }

    public bool Saved { get; private set; }

    public IReadOnlyList<Account> Accounts => _accounts;

    private void BuildLayout()
    {
        _grid.SetBounds(0, 0, Theme.S(1321), Theme.S(341));
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.Font = Theme.F(28, true);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        AddColumn("序号", 40);
        AddColumn("名称", 90);
        AddColumn("期货账号", 100);
        AddColumn("BrokerID", 70);
        AddColumn("AppID", 100);
        AddColumn("行情地址", 200);
        AddColumn("交易地址", 200);
        AddColumn("状态", 60);

        _grid.SelectionChanged += (_, _) => LoadSelectedAccount();
        _grid.CellDoubleClick += (_, _) => LoadSelectedAccount();

        var deleteItem = new ToolStripMenuItem("删除账号");
        deleteItem.Click += (_, _) => DeleteSelectedAccount();
        var menu = new ContextMenuStrip();
        menu.Items.Add(deleteItem);
        _grid.ContextMenuStrip = menu;

        var panel1 = new Panel
        {
            Left = 0,
            Top = Theme.S(351),
            Width = Theme.S(601),
            Height = Theme.S(321),
            BackColor = Theme.MenuBack,
        };

        _name.SetBounds(0, Theme.S(9), Theme.S(550), Theme.S(38));
        _userId.SetBounds(0, Theme.S(53), Theme.S(550), Theme.S(38));
        _brokerId.SetBounds(0, Theme.S(97), Theme.S(550), Theme.S(38));
        _appId.SetBounds(0, Theme.S(141), Theme.S(550), Theme.S(38));
        _marketFront.SetBounds(2, Theme.S(185), Theme.S(550), Theme.S(38));
        _tradeFront.SetBounds(2, Theme.S(229), Theme.S(550), Theme.S(38));

        foreach (var edit in new[] { _name, _userId, _brokerId, _appId, _marketFront, _tradeFront })
        {
            edit.Font = Theme.F(24);
            edit.BorderStyle = BorderStyle.FixedSingle;
            panel1.Controls.Add(edit);
        }

        _name.PlaceholderText = "用户名字";
        _userId.PlaceholderText = "期货账号";
        _brokerId.PlaceholderText = "BrokerID";
        _appId.PlaceholderText = "AppID（柜台认证，非授权码）";
        _marketFront.PlaceholderText = "行情地址，如 tcp://180.168.146.187:10211";
        _tradeFront.PlaceholderText = "交易地址，如 tcp://180.168.146.187:10130";

        var save = new Button
        {
            Left = Theme.S(145),
            Top = Theme.S(279),
            Width = Theme.S(176),
            Height = Theme.S(30),
            Text = "添加/修改账号",
            Font = Theme.F(24),
            FlatStyle = FlatStyle.System,
        };
        save.Click += (_, _) => SaveAccount();
        panel1.Controls.Add(save);

        var panel2 = new Panel
        {
            Left = Theme.S(947),
            Top = Theme.S(351),
            Width = Theme.S(374),
            Height = Theme.S(321),
            BackColor = Theme.MenuBack,
        };

        _leftUsers.SetBounds(Theme.S(41), Theme.S(9), Theme.S(160), Theme.S(258));
        _rightUsers.SetBounds(Theme.S(191), Theme.S(9), Theme.S(160), Theme.S(258));
        foreach (var list in new[] { _leftUsers, _rightUsers })
        {
            list.Font = Theme.F(20);
            list.IntegralHeight = false;
            panel2.Controls.Add(list);
        }

        _leftUsers.DoubleClick += (_, _) => MoveAccount(_leftUsers, _rightUsers);
        _rightUsers.DoubleClick += (_, _) => MoveAccount(_rightUsers, _leftUsers);

        var copy = new Button
        {
            Left = Theme.S(125),
            Top = Theme.S(279),
            Width = Theme.S(156),
            Height = Theme.S(30),
            Text = "复制合约表",
            Font = Theme.F(24),
            FlatStyle = FlatStyle.System,
        };
        copy.Click += (_, _) => CopyAccounts();
        panel2.Controls.Add(copy);

        var panel3 = new Panel
        {
            Left = Theme.S(602),
            Top = Theme.S(351),
            Width = Theme.S(335),
            Height = Theme.S(321),
            BackColor = Theme.MenuBack,
        };

        _parse.Multiline = true;
        _parse.ScrollBars = ScrollBars.Vertical;
        _parse.SetBounds(Theme.S(8), Theme.S(8), Theme.S(319), Theme.S(222));
        _parse.Font = Theme.F(20);
        _parse.BorderStyle = BorderStyle.FixedSingle;
        _parse.PlaceholderText = "粘贴本文进行解析...（每行：名称 账号 BrokerID AppID 行情地址 交易地址）";
        panel3.Controls.Add(_parse);

        var parseButton = new Button
        {
            Left = Theme.S(8),
            Top = Theme.S(239),
            Width = Theme.S(319),
            Height = Theme.S(30),
            Text = "解析粘贴内容",
            Font = Theme.F(24),
            FlatStyle = FlatStyle.System,
        };
        parseButton.Click += (_, _) => ParsePastedAccounts();
        panel3.Controls.Add(parseButton);

        Controls.AddRange([_grid, panel1, panel2, panel3]);
    }

    private void AddColumn(string header, int width)
    {
        _grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = Theme.S(width),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
            });
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        for (var i = 0; i < _accounts.Count; i++)
        {
            var account = _accounts[i];
            _grid.Rows.Add(
                (i + 1).ToString(),
                string.IsNullOrWhiteSpace(account.DisplayName) ? account.UserId : account.DisplayName,
                account.UserId,
                account.BrokerId,
                account.AppId,
                account.MarketFront,
                account.TradeFront,
                "已配置");
        }

        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
        }
    }

    private void PopulateLists()
    {
        _leftUsers.Items.Clear();
        foreach (var account in _accounts)
        {
            _leftUsers.Items.Add(DisplayName(account));
        }

        _log.Append($"账号编辑器：已载入 {_accounts.Count} 个账号。");
    }

    private void LoadSelectedAccount()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            return;
        }

        var index = _grid.SelectedRows[0].Index;
        if (index < 0 || index >= _accounts.Count)
        {
            return;
        }

        _editingIndex = index;
        var account = _accounts[index];
        _name.Text = account.DisplayName;
        _userId.Text = account.UserId;
        _brokerId.Text = account.BrokerId;
        _appId.Text = account.AppId;
        _marketFront.Text = account.MarketFront;
        _tradeFront.Text = account.TradeFront;
    }

    private void SaveAccount()
    {
        var userId = _userId.Text.Trim();
        if (userId.Length == 0)
        {
            MessageBox.Show(this, "期货账号不能为空。", "账号编辑器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var account = new Account
        {
            DisplayName = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : userId,
            UserId = userId,
            BrokerId = _brokerId.Text.Trim(),
            AppId = _appId.Text.Trim(),
            MarketFront = _marketFront.Text.Trim(),
            TradeFront = _tradeFront.Text.Trim(),
        };

        if (_editingIndex >= 0 && _editingIndex < _accounts.Count)
        {
            _accounts[_editingIndex] = account;
            _log.Append($"账号已修改：{account.DisplayName} ({account.BrokerId}/{account.UserId})");
        }
        else
        {
            _accounts.Add(account);
            _log.Append($"账号已新增：{account.DisplayName} ({account.BrokerId}/{account.UserId})");
        }

        Persist();
        PopulateGrid();
        PopulateLists();
        ClearEditor();
    }

    private void DeleteSelectedAccount()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            return;
        }

        var index = _grid.SelectedRows[0].Index;
        if (index < 0 || index >= _accounts.Count)
        {
            return;
        }

        var account = _accounts[index];
        if (MessageBox.Show(
                this,
                $"确认删除账号 {DisplayName(account)}？",
                "账号编辑器",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _accounts.RemoveAt(index);
        _log.Append($"账号已删除：{DisplayName(account)}");
        Persist();
        PopulateGrid();
        PopulateLists();
        ClearEditor();
    }

    private void ParsePastedAccounts()
    {
        var text = _parse.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var added = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(['\t', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var account = new Account
            {
                DisplayName = parts.Length > 0 ? parts[0] : parts[1],
                UserId = parts.Length > 1 ? parts[1] : string.Empty,
                BrokerId = parts.Length > 2 ? parts[2] : string.Empty,
                AppId = parts.Length > 3 ? parts[3] : string.Empty,
                MarketFront = parts.Length > 4 ? parts[4] : string.Empty,
                TradeFront = parts.Length > 5 ? parts[5] : string.Empty,
            };

            if (account.UserId.Length == 0)
            {
                continue;
            }

            _accounts.Add(account);
            added++;
        }

        if (added == 0)
        {
            _log.Append("粘贴内容未解析出账号，请按“名称 账号 BrokerID AppID 行情地址 交易地址”每行一条。");
            return;
        }

        _log.Append($"粘贴解析完成：新增 {added} 个账号。");
        Persist();
        PopulateGrid();
        PopulateLists();
        _parse.Clear();
    }

    private void MoveAccount(ListBox from, ListBox to)
    {
        if (from.SelectedItem is not string item)
        {
            return;
        }

        from.Items.Remove(item);
        to.Items.Add(item);
    }

    private void CopyAccounts()
    {
        foreach (var item in _leftUsers.SelectedItems.Cast<string>().ToList())
        {
            if (!_rightUsers.Items.Contains(item))
            {
                _rightUsers.Items.Add(item);
            }
        }
    }

    private void Persist()
    {
        try
        {
            UserRepository.Save(_usersPath, _accounts);
            Saved = true;
        }
        catch (Exception ex)
        {
            _log.Append($"保存 Users.xml 失败：{ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "保存账号失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ClearEditor()
    {
        _editingIndex = -1;
        _name.Clear();
        _userId.Clear();
        _brokerId.Clear();
        _appId.Clear();
        _marketFront.Clear();
        _tradeFront.Clear();
    }

    private static string DisplayName(Account account) =>
        string.IsNullOrWhiteSpace(account.DisplayName) ? account.UserId : account.DisplayName;
}
