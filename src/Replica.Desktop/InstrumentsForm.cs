namespace Replica.Desktop;

/// <summary>
/// 原软件 TInstrumentsWin 的复刻：436×594、bsSingle，单个 LvHyList
/// （ID 191 / Name 240，新宋体 -24，GridLines、ReadOnly、RowSelect）。
/// 双击合约送回主窗行情梯。
/// </summary>
internal sealed class InstrumentsForm : Form
{
    private readonly MainForm _main;
    private readonly ReplicaContext _ctx;
    private readonly ListView _list = new();

    internal InstrumentsForm(MainForm main, ReplicaContext context)
    {
        _main = main;
        _ctx = context;

        Text = "合约列表";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(Theme.S(436), Theme.S(594));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = SystemColors.Control;
        Font = Theme.F(18, false, "Segoe UI");

        _list.Name = "LvHyList";
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.GridLines = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Font = Theme.F(24, false, "新宋体");
        _list.Columns.Add("ID", Theme.S(191), HorizontalAlignment.Left);
        _list.Columns.Add("Name", Theme.S(240), HorizontalAlignment.Left);
        _list.DoubleClick += (_, _) => SendSelected();

        foreach (var instrument in _ctx.Instruments.All)
        {
            var item = new ListViewItem(instrument.InstrumentId);
            item.SubItems.Add(instrument.Name ?? string.Empty);
            _list.Items.Add(item);
        }

        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendSelected();
                e.Handled = true;
            }
        };

        Controls.Add(_list);
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
