using Replica.Core.Abstractions;
using Replica.Core.Models;

namespace Replica.Ctp;

/// <summary>
/// CTP 行情适配器，对应原软件 thostmduserapi_se.dll 的行情链路。
/// 复刻版当前未生成原生桥接层，ConnectAsync 会给出明确的环境提示。
/// </summary>
public sealed class CtpMarketDataClient : IMarketDataClient
{
    private bool _disposed;

#pragma warning disable CS0067 // 原生桥接层接入后触发。
    public event EventHandler<Quote>? QuoteUpdated;

    public event EventHandler<ConnectionState>? ConnectionChanged;
#pragma warning restore CS0067

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);

        // 先校验前置地址格式，再检查原生库，便于定位配置错误。
        _ = CtpFrontAddress.Parse(account.MarketFront, nameof(account.MarketFront));
        CtpEnvironment.EnsureSupported(CtpEnvironment.MarketApiLibrary);

        IsConnected = true;
        ConnectionChanged?.Invoke(this, ConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(
        IEnumerable<string> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        var ids = instrumentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray();

        if (ids.Length == 0)
        {
            return Task.CompletedTask;
        }

        // 原生桥接层将在这里调用 SubscribeMarketData。
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return Task.CompletedTask;
        }

        IsConnected = false;
        ConnectionChanged?.Invoke(this, ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("行情连接尚未建立");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CtpMarketDataClient));
        }
    }
}
