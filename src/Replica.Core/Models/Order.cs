namespace Replica.Core.Models;

/// <summary>委托。状态由交易客户端更新。</summary>
public sealed class Order
{
    public required string OrderId { get; init; }

    public required string InstrumentId { get; init; }

    public OrderDirection Direction { get; init; }

    public OffsetFlag Offset { get; init; }

    public OrderType Type { get; init; }

    public double Price { get; init; }

    public int Volume { get; init; }

    public int TradedVolume { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public string StatusMessage { get; set; } = string.Empty;

    public DateTime InsertTime { get; init; } = DateTime.Now;

    public DateTime UpdateTime { get; set; } = DateTime.Now;

    public override string ToString() =>
        $"{OrderId} {InstrumentId} {Direction} {Offset} {Volume}@{Price:0.##} [{Status}]";
}
