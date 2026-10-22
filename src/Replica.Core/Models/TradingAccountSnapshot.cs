namespace Replica.Core.Models;

/// <summary>资金账户快照。TqSdk 等交易适配器会持续推送。</summary>
public sealed record TradingAccountSnapshot
{
    /// <summary>动态权益。</summary>
    public double Balance { get; init; }

    /// <summary>可用资金。</summary>
    public double Available { get; init; }

    public double Margin { get; init; }

    public double FloatProfit { get; init; }

    public double CloseProfit { get; init; }

    public double Commission { get; init; }

    public double RiskRatio { get; init; }

    public DateTime UpdateTime { get; init; } = DateTime.Now;

    public static TradingAccountSnapshot Empty { get; } = new();
}
