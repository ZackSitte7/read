namespace Replica.Core.Models;

/// <summary>平仓记录，对应原软件 TCloseRecordWin 中展示的一行。</summary>
public sealed record CloseRecord
{
    public required string InstrumentId { get; init; }

    public PositionDirection Direction { get; init; }

    public double OpenPrice { get; init; }

    public double ClosePrice { get; init; }

    public int Volume { get; init; } = 1;

    public DateTime Time { get; init; } = DateTime.Now;

    public double Pnl =>
        Direction == PositionDirection.Long
            ? (ClosePrice - OpenPrice) * Volume
            : (OpenPrice - ClosePrice) * Volume;
}
