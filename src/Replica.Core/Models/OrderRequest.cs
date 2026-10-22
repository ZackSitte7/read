namespace Replica.Core.Models;

/// <summary>下单请求。</summary>
public sealed record OrderRequest
{
    public required string InstrumentId { get; init; }

    public OrderDirection Direction { get; init; }

    public OffsetFlag Offset { get; init; }

    public OrderType Type { get; init; } = OrderType.Limit;

    /// <summary>限价；市价单忽略此值。</summary>
    public double Price { get; init; }

    public int Volume { get; init; } = 1;
}
