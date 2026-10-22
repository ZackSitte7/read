using Replica.Core.Abstractions;
using Replica.Core.Models;

namespace Replica.Ctp;

/// <summary>
/// CTP 交易适配器，对应原软件 thosttraderapi_se.dll 的交易链路。
/// 复刻版当前未生成原生桥接层，ConnectAsync 会给出明确的环境提示。
/// </summary>
public sealed class CtpTradeClient : ITradeClient
{
    private bool _disposed;

#pragma warning disable CS0067 // 原生桥接层接入后触发。
    public event EventHandler<Order>? OrderUpdated;

    public event EventHandler<Trade>? TradeReceived;

    public event EventHandler<Position>? PositionUpdated;

    public event EventHandler<ConnectionState>? ConnectionChanged;
#pragma warning restore CS0067

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(account);

        _ = CtpFrontAddress.Parse(account.TradeFront, nameof(account.TradeFront));
        CtpEnvironment.EnsureSupported(CtpEnvironment.TradeApiLibrary);

        IsConnected = true;
        ConnectionChanged?.Invoke(this, ConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task<Order> PlaceOrderAsync(
        OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(request);

        // 原生桥接层将在这里调用 ReqOrderInsert，并把 OnRtnOrder 映射为 OrderUpdated。
        throw new NotSupportedException("CTP 原生桥接层尚未接入，无法提交委托。");
    }

    public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        // 原生桥接层将在这里调用 ReqOrderAction。
        throw new NotSupportedException("CTP 原生桥接层尚未接入，无法撤单。");
    }

    public Task<IReadOnlyList<Position>> QueryPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        // 原生桥接层将在这里调用 ReqQryInvestorPosition。
        throw new NotSupportedException("CTP 原生桥接层尚未接入，无法查询持仓。");
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
            throw new InvalidOperationException("交易连接尚未建立");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CtpTradeClient));
        }
    }
}
