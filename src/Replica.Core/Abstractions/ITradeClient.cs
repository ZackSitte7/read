using Replica.Core.Models;

namespace Replica.Core.Abstractions;

/// <summary>交易客户端抽象。Mock 与 CTP 适配器实现同一接口。</summary>
public interface ITradeClient : IAsyncDisposable
{
    event EventHandler<Order>? OrderUpdated;

    event EventHandler<Trade>? TradeReceived;

    event EventHandler<Position>? PositionUpdated;

    /// <summary>资金权益变化；不支持资金查询的适配器不会触发。</summary>
    event EventHandler<TradingAccountSnapshot>? AccountUpdated;

    event EventHandler<ConnectionState>? ConnectionChanged;

    bool IsConnected { get; }

    Task ConnectAsync(Account account, CancellationToken cancellationToken = default);

    Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);

    Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Position>> QueryPositionsAsync(CancellationToken cancellationToken = default);

    /// <summary>查询资金权益；返回 null 表示该适配器不支持。</summary>
    Task<TradingAccountSnapshot?> QueryAccountAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
