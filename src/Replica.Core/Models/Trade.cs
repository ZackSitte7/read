namespace Replica.Core.Models;

/// <summary>成交回报。</summary>
public sealed record Trade
{
    public required string TradeId { get; init; }

    public required string OrderId { get; init; }

    public required string InstrumentId { get; init; }

    public OrderDirection Direction { get; init; }

    public OffsetFlag Offset { get; init; }

    public double Price { get; init; }

    public int Volume { get; init; }

    public DateTime TradeTime { get; init; } = DateTime.Now;
}
