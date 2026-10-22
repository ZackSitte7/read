namespace Replica.Core.Models;

/// <summary>行情快照。</summary>
public sealed record Quote
{
    public required string InstrumentId { get; init; }

    public double LastPrice { get; init; }

    public double BidPrice { get; init; }

    public int BidVolume { get; init; }

    public double AskPrice { get; init; }

    public int AskVolume { get; init; }

    /// <summary>买盘深度，按买一、买二……顺序排列。</summary>
    public IReadOnlyList<DepthLevel> Bids { get; init; } = [];

    /// <summary>卖盘深度，按卖一、卖二……顺序排列。</summary>
    public IReadOnlyList<DepthLevel> Asks { get; init; } = [];

    public int Volume { get; init; }

    public double OpenInterest { get; init; }

    public double PreSettlementPrice { get; init; }

    public double UpperLimitPrice { get; init; }

    public double LowerLimitPrice { get; init; }

    public DateTime UpdateTime { get; init; } = DateTime.Now;
}
