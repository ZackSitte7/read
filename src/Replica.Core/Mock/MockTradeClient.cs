using Replica.Core.Abstractions;
using Replica.Core.Models;

namespace Replica.Core.Mock;

/// <summary>
/// 模拟交易客户端：市价单与可成交限价单立即成交，其余委托挂在本地订单簿，
/// 行情价格穿过委托价时成交。用于在没有 CTP 前置的情况下验证登录、下单、成交、持仓链路。
/// </summary>
public sealed class MockTradeClient : ITradeClient
{
    private readonly Func<string, Quote?> _quoteProvider;
    private readonly Dictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string InstrumentId, PositionDirection Direction), Position> _positions = [];
    private readonly List<CloseRecord> _closes = [];
    private readonly object _gate = new();

    private int _orderSequence;
    private int _tradeSequence;
    private bool _disposed;

    public MockTradeClient(Func<string, Quote?> quoteProvider)
    {
        _quoteProvider = quoteProvider;
    }

    public event EventHandler<Order>? OrderUpdated;

    public event EventHandler<Trade>? TradeReceived;

    public event EventHandler<Position>? PositionUpdated;

    public event EventHandler<ConnectionState>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ConnectionChanged?.Invoke(this, ConnectionState.Connecting);
        IsConnected = true;
        ConnectionChanged?.Invoke(this, ConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task<Order> PlaceOrderAsync(
        OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Order order;
        lock (_gate)
        {
            order = new Order
            {
                OrderId = $"MOCK{++_orderSequence:D6}",
                InstrumentId = request.InstrumentId,
                Direction = request.Direction,
                Offset = request.Offset,
                Type = request.Type,
                Price = request.Price,
                Volume = request.Volume,
            };
            _orders[order.OrderId] = order;
        }

        RaiseOrder(order);

        var quote = _quoteProvider(request.InstrumentId);
        if (quote is null)
        {
            Reject(order, "无可用行情");
            return Task.FromResult(order);
        }

        if (request.Type == OrderType.Market)
        {
            var price = request.Direction == OrderDirection.Buy ? quote.AskPrice : quote.BidPrice;
            Fill(order, price);
            return Task.FromResult(order);
        }

        var marketable = request.Direction == OrderDirection.Buy
            ? quote.AskPrice <= request.Price
            : quote.BidPrice >= request.Price;

        if (marketable)
        {
            var price = request.Direction == OrderDirection.Buy
                ? Math.Min(request.Price, quote.AskPrice)
                : Math.Max(request.Price, quote.BidPrice);
            Fill(order, price);
        }

        return Task.FromResult(order);
    }

    /// <summary>行情更新时撮合本地挂单，对应原软件中报单随行情被动成交的行为。</summary>
    public void OnQuote(Quote quote)
    {
        List<(Order Order, double Price)> matches = [];

        lock (_gate)
        {
            foreach (var order in _orders.Values)
            {
                if (order.Status != OrderStatus.Pending ||
                    order.Type != OrderType.Limit ||
                    !string.Equals(order.InstrumentId, quote.InstrumentId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double? fillPrice = null;
                if (order.Direction == OrderDirection.Buy && quote.AskPrice <= order.Price)
                {
                    fillPrice = Math.Min(order.Price, quote.AskPrice);
                }
                else if (order.Direction == OrderDirection.Sell && quote.BidPrice >= order.Price)
                {
                    fillPrice = Math.Max(order.Price, quote.BidPrice);
                }

                if (fillPrice is { } price)
                {
                    // 先占位，避免同一笔委托被连续两个行情 tick 重复成交。
                    order.Status = OrderStatus.PartiallyFilled;
                    matches.Add((order, price));
                }
            }
        }

        foreach (var (order, price) in matches)
        {
            Fill(order, price);
        }
    }

    public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Order? order;
        lock (_gate)
        {
            if (!_orders.TryGetValue(orderId, out order) || order.Status != OrderStatus.Pending)
            {
                return Task.CompletedTask;
            }

            order.Status = OrderStatus.Cancelled;
            order.StatusMessage = "已撤单";
            order.UpdateTime = DateTime.Now;
        }

        RaiseOrder(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Position>> QueryPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            IReadOnlyList<Position> snapshot = _positions.Values
                .Where(p => p.Volume > 0)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(snapshot);
        }
    }

    /// <summary>平仓记录快照，供平仓记录窗展示。</summary>
    public IReadOnlyList<CloseRecord> GetCloseRecords()
    {
        lock (_gate)
        {
            return _closes.ToArray();
        }
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

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    private void Fill(Order order, double fillPrice)
    {
        Position? affected = null;
        var rejected = false;

        lock (_gate)
        {
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Filled)
            {
                return;
            }

            if (order.Offset == OffsetFlag.Open)
            {
                var direction = order.Direction == OrderDirection.Buy
                    ? PositionDirection.Long
                    : PositionDirection.Short;
                affected = GetOrCreatePosition(order.InstrumentId, direction);
                ApplyOpen(affected, fillPrice, order.Volume);
            }
            else
            {
                var direction = order.Direction == OrderDirection.Buy
                    ? PositionDirection.Short
                    : PositionDirection.Long;
                affected = GetOrCreatePosition(order.InstrumentId, direction);

                if (affected.Volume < order.Volume)
                {
                    order.Status = OrderStatus.Rejected;
                    order.StatusMessage = "可平持仓不足";
                    order.UpdateTime = DateTime.Now;
                    rejected = true;
                }
                else
                {
                    var openPrice = affected.AvgPrice;
                    ApplyClose(affected, fillPrice, order.Volume);
                    _closes.Add(
                        new CloseRecord
                        {
                            InstrumentId = order.InstrumentId,
                            Direction = direction,
                            OpenPrice = openPrice,
                            ClosePrice = fillPrice,
                            Volume = order.Volume,
                            Time = DateTime.Now,
                        });
                    if (_closes.Count > 4000)
                    {
                        _closes.RemoveRange(0, _closes.Count - 4000);
                    }
                }
            }

            if (!rejected)
            {
                order.TradedVolume = order.Volume;
                order.Status = OrderStatus.Filled;
                order.StatusMessage = "全部成交";
                order.UpdateTime = DateTime.Now;
            }
        }

        if (rejected)
        {
            RaiseOrder(order);
            return;
        }

        var trade = new Trade
        {
            TradeId = $"T{++_tradeSequence:D6}",
            OrderId = order.OrderId,
            InstrumentId = order.InstrumentId,
            Direction = order.Direction,
            Offset = order.Offset,
            Price = fillPrice,
            Volume = order.Volume,
        };

        RaiseOrder(order);
        TradeReceived?.Invoke(this, trade);

        if (affected is not null)
        {
            RaisePosition(affected);
        }
    }

    private void Reject(Order order, string reason)
    {
        lock (_gate)
        {
            order.Status = OrderStatus.Rejected;
            order.StatusMessage = reason;
            order.UpdateTime = DateTime.Now;
        }

        RaiseOrder(order);
    }

    private Position GetOrCreatePosition(string instrumentId, PositionDirection direction)
    {
        var key = (instrumentId, direction);
        if (!_positions.TryGetValue(key, out var position))
        {
            position = new Position
            {
                InstrumentId = instrumentId,
                Direction = direction,
                LastPrice = _quoteProvider(instrumentId)?.LastPrice ?? 0,
            };
            _positions[key] = position;
        }

        position.LastPrice = _quoteProvider(instrumentId)?.LastPrice ?? position.LastPrice;
        return position;
    }

    private static void ApplyOpen(Position position, double price, int volume)
    {
        var total = position.Volume + volume;
        position.AvgPrice = total == 0
            ? 0
            : (position.AvgPrice * position.Volume + price * volume) / total;
        position.Volume = total;
    }

    private static void ApplyClose(Position position, double price, int volume)
    {
        position.Volume -= volume;
        position.LastPrice = price;
        if (position.Volume <= 0)
        {
            position.Volume = 0;
            position.AvgPrice = 0;
        }
    }

    private static Position Clone(Position position) => new()
    {
        InstrumentId = position.InstrumentId,
        Direction = position.Direction,
        Volume = position.Volume,
        AvgPrice = position.AvgPrice,
        LastPrice = position.LastPrice,
    };

    private void RaiseOrder(Order order) => OrderUpdated?.Invoke(this, order);

    private void RaisePosition(Position position) => PositionUpdated?.Invoke(this, Clone(position));

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MockTradeClient));
        }
    }
}
