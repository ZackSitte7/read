namespace Replica.Core.Models;

/// <summary>买卖方向。</summary>
public enum OrderDirection
{
    Buy,
    Sell,
}

/// <summary>开平标志。</summary>
public enum OffsetFlag
{
    Open,
    Close,
    CloseToday,
    CloseYesterday,
}

/// <summary>委托类型；市价单价格填 0。</summary>
public enum OrderType
{
    Limit,
    Market,

    /// <summary>立即成交剩余撤销（FAK）。</summary>
    Fak,

    /// <summary>全部成交否则撤销（FOK）。</summary>
    Fok,
}

/// <summary>委托状态。</summary>
public enum OrderStatus
{
    Pending,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
}

/// <summary>持仓方向。</summary>
public enum PositionDirection
{
    Long,
    Short,
}

/// <summary>连接状态。</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted,
}
