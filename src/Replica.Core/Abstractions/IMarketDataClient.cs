using Replica.Core.Models;

namespace Replica.Core.Abstractions;

/// <summary>行情客户端抽象。Mock 与 CTP 适配器实现同一接口。</summary>
public interface IMarketDataClient : IAsyncDisposable
{
    event EventHandler<Quote>? QuoteUpdated;

    event EventHandler<ConnectionState>? ConnectionChanged;

    bool IsConnected { get; }

    Task ConnectAsync(Account account, CancellationToken cancellationToken = default);

    Task SubscribeAsync(IEnumerable<string> instrumentIds, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
