namespace Replica.Core.Models;

/// <summary>合约静态信息，对应原软件 Instruments.xml 的 Instrument 节点。</summary>
public sealed record Instrument
{
    public required string InstrumentId { get; init; }

    /// <summary>合约中文名，对应原软件合约列表的 Name 列。</summary>
    public string Name { get; init; } = string.Empty;

    public string ExchangeId { get; init; } = string.Empty;

    public string ProductId { get; init; } = string.Empty;

    public int VolumeMultiple { get; init; } = 1;

    public double PriceTick { get; init; } = 0.01;

    public double LongMarginRatio { get; init; } = 0.1;

    public double ShortMarginRatio { get; init; } = 0.1;

    public string ExpireDate { get; init; } = string.Empty;

    public override string ToString() => InstrumentId;
}
