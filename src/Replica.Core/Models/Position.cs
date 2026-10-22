namespace Replica.Core.Models;

/// <summary>持仓。</summary>
public sealed class Position
{
    public required string InstrumentId { get; init; }

    public PositionDirection Direction { get; init; }

    public int Volume { get; set; }

    public double AvgPrice { get; set; }

    public double LastPrice { get; set; }

    public double UnrealizedPnl =>
        Direction == PositionDirection.Long
            ? (LastPrice - AvgPrice) * Volume
            : (AvgPrice - LastPrice) * Volume;

    public override string ToString() =>
        $"{InstrumentId} {Direction} {Volume}手 均价{AvgPrice:0.##} 浮盈{UnrealizedPnl:0.##}";
}
