namespace Replica.Core.Models;

/// <summary>
/// 交易账户。复刻版只保留期货账户登录所需信息，
/// 不包含厂商授权码、MAC 绑定等认证字段。
/// </summary>
public sealed record Account
{
    public required string BrokerId { get; init; }

    public required string UserId { get; init; }

    public string Password { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>交易前置地址。</summary>
    public string TradeFront { get; init; } = string.Empty;

    /// <summary>行情前置地址。</summary>
    public string MarketFront { get; init; } = string.Empty;

    /// <summary>终端标识，仅用于展示与日志，不参与任何授权校验。</summary>
    public string TerminalId { get; init; } = "replica";
}
