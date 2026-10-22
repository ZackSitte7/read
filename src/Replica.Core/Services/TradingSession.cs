using System.Collections.Concurrent;
using Replica.Core.Abstractions;
using Replica.Core.Models;

namespace Replica.Core.Services;

/// <summary>
/// 交易会话：组合行情与交易客户端，向上层暴露统一事件与下单入口。
/// 原软件中这一层还包含厂商授权与云端风控，复刻版只保留交易主链路。
/// </summary>
public sealed class TradingSession : IAsyncDisposable
{
    private readonly IMarketDataClient _market;
    private readonly ITradeClient _trade;
    private readonly ConcurrentDictionary<string, Quote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public TradingSession(IMarketDataClient market, ITradeClient trade)
    {
        _market = market;
        _trade = trade;

        _market.QuoteUpdated += HandleQuoteUpdated;
        _market.ConnectionChanged += HandleConnectionChanged;
        _trade.OrderUpdated += HandleOrderUpdated;
        _trade.TradeReceived += HandleTradeReceived;
        _trade.PositionUpdated += HandlePositionUpdated;
        _trade.AccountUpdated += HandleAccountUpdated;
        _trade.ConnectionChanged += HandleConnectionChanged;
    }

    public event EventHandler<Quote>? QuoteUpdated;

    public event EventHandler<Order>? OrderUpdated;

    public event EventHandler<Trade>? TradeReceived;

    public event EventHandler<Position>? PositionUpdated;

    public event EventHandler<TradingAccountSnapshot>? AccountUpdated;

    public event EventHandler<ConnectionState>? ConnectionChanged;

    public event EventHandler<string>? Log;

    public Account? Account { get; private set; }

    public bool IsConnected => _market.IsConnected && _trade.IsConnected;

    public IReadOnlyCollection<string> Subscriptions => _subscriptions;

    public async Task ConnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Account = account;

        Log?.Invoke(this, $"连接交易前置 {account.TradeFront}");
        await _trade.ConnectAsync(account, cancellationToken).ConfigureAwait(false);

        Log?.Invoke(this, $"连接行情前置 {account.MarketFront}");
        await _market.ConnectAsync(account, cancellationToken).ConfigureAwait(false);

        Log?.Invoke(this, $"登录成功：{account.BrokerId}/{account.UserId}");
    }

    public async Task SubscribeAsync(
        IEnumerable<string> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var ids = instrumentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        foreach (var id in ids)
        {
            _subscriptions.Add(id);
        }

        await _market.SubscribeAsync(ids, cancellationToken).ConfigureAwait(false);
        Log?.Invoke(this, $"已订阅 {ids.Length} 个合约：{string.Join(", ", ids)}");
    }

    public Task<Order> PlaceOrderAsync(
        OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsConnected)
        {
            throw new InvalidOperationException("交易会话尚未连接");
        }

        if (string.IsNullOrWhiteSpace(request.InstrumentId))
        {
            throw new ArgumentException("合约代码不能为空", nameof(request));
        }

        if (request.Volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "下单数量必须大于 0");
        }

        if (request.Type == OrderType.Limit && request.Price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "限价单价格必须大于 0");
        }

        Log?.Invoke(
            this,
            $"提交委托 {request.InstrumentId} {request.Direction} {request.Offset} " +
            $"{request.Volume} 手 @ {(request.Type == OrderType.Market ? "市价" : request.Price.ToString("0.##"))}");

        return _trade.PlaceOrderAsync(request, cancellationToken);
    }

    public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new ArgumentException("委托编号不能为空", nameof(orderId));
        }

        return _trade.CancelOrderAsync(orderId, cancellationToken);
    }

    public Task<IReadOnlyList<Position>> QueryPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _trade.QueryPositionsAsync(cancellationToken);
    }

    public Task<TradingAccountSnapshot?> QueryAccountAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _trade.QueryAccountAsync(cancellationToken);
    }

    public Quote? GetQuote(string instrumentId) =>
        _quotes.TryGetValue(instrumentId, out var quote) ? quote : null;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _market.QuoteUpdated -= HandleQuoteUpdated;
        _market.ConnectionChanged -= HandleConnectionChanged;
        _trade.OrderUpdated -= HandleOrderUpdated;
        _trade.TradeReceived -= HandleTradeReceived;
        _trade.PositionUpdated -= HandlePositionUpdated;
        _trade.AccountUpdated -= HandleAccountUpdated;
        _trade.ConnectionChanged -= HandleConnectionChanged;

        try
        {
            await _market.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"断行行情连接失败：{ex.Message}");
        }

        try
        {
            await _trade.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"断开交易连接失败：{ex.Message}");
        }

        await _market.DisposeAsync().ConfigureAwait(false);
        await _trade.DisposeAsync().ConfigureAwait(false);
    }

    private void HandleQuoteUpdated(object? sender, Quote quote)
    {
        _quotes[quote.InstrumentId] = quote;
        QuoteUpdated?.Invoke(this, quote);
    }

    private void HandleOrderUpdated(object? sender, Order order) =>
        OrderUpdated?.Invoke(this, order);

    private void HandleTradeReceived(object? sender, Trade trade) =>
        TradeReceived?.Invoke(this, trade);

    private void HandlePositionUpdated(object? sender, Position position) =>
        PositionUpdated?.Invoke(this, position);

    private void HandleAccountUpdated(object? sender, TradingAccountSnapshot account) =>
        AccountUpdated?.Invoke(this, account);

    private void HandleConnectionChanged(object? sender, ConnectionState state) =>
        ConnectionChanged?.Invoke(this, state);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TradingSession));
        }
    }
}
