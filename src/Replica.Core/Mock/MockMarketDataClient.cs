using System.Collections.Concurrent;
using Replica.Core.Abstractions;
using Replica.Core.Models;
using Replica.Core.Services;

namespace Replica.Core.Mock;

/// <summary>
/// 模拟行情源：按合约价格最小变动单位做随机游走，用于在无 CTP 环境下验证主链路。
/// </summary>
public sealed class MockMarketDataClient : IMarketDataClient
{
    private readonly InstrumentRepository _instruments;
    private readonly ConcurrentDictionary<string, Quote> _quotes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new(20261022);
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _pump;
    private bool _disposed;

    public MockMarketDataClient(InstrumentRepository instruments)
    {
        _instruments = instruments;
    }

    public event EventHandler<Quote>? QuoteUpdated;

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

    public Task SubscribeAsync(
        IEnumerable<string> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        foreach (var id in instrumentIds)
        {
            EnsureQuote(id.Trim());
        }

        EnsurePump();
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;

        CancellationTokenSource? cts;
        Task? pump;
        lock (_gate)
        {
            cts = _cts;
            pump = _pump;
            _cts = null;
            _pump = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                if (pump is not null)
                {
                    await pump.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }

            cts.Dispose();
        }

        ConnectionChanged?.Invoke(this, ConnectionState.Disconnected);
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

    public Quote? GetQuote(string instrumentId) =>
        _quotes.TryGetValue(instrumentId, out var quote) ? quote : null;

    private void EnsureQuote(string instrumentId)
    {
        _quotes.GetOrAdd(instrumentId, id =>
        {
            var instrument = _instruments.Find(id);
            var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 1d;
            var basePrice = Math.Round((1000 + StableHash(id) % 4000) / tick) * tick;

            return new Quote
            {
                InstrumentId = id,
                LastPrice = basePrice,
                BidPrice = basePrice - tick,
                BidVolume = 10,
                AskPrice = basePrice + tick,
                AskVolume = 10,
                Bids = BuildDepth(basePrice - tick, tick, 5),
                Asks = BuildDepth(basePrice + tick, tick, 5),
                Volume = 0,
                OpenInterest = 1000,
                PreSettlementPrice = basePrice,
                UpperLimitPrice = Math.Round(basePrice * 1.1 / tick) * tick,
                LowerLimitPrice = Math.Round(basePrice * 0.9 / tick) * tick,
                UpdateTime = DateTime.Now,
            };
        });
    }

    private void EnsurePump()
    {
        lock (_gate)
        {
            if (_pump is not null || _disposed)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _pump = Task.Run(() => PumpAsync(_cts.Token));
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(300));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var id in _quotes.Keys)
                {
                    if (!_quotes.TryGetValue(id, out var current))
                    {
                        continue;
                    }

                    var instrument = _instruments.Find(id);
                    var tick = instrument?.PriceTick is > 0 ? instrument.PriceTick : 1d;
                    var move = _random.Next(-2, 3) * tick;
                    var last = Math.Max(tick, current.LastPrice + move);

                    var next = current with
                    {
                        LastPrice = last,
                        BidPrice = last - tick,
                        AskPrice = last + tick,
                        BidVolume = _random.Next(1, 50),
                        AskVolume = _random.Next(1, 50),
                        Bids = BuildDepth(last - tick, tick, 5),
                        Asks = BuildDepth(last + tick, tick, 5),
                        Volume = current.Volume + _random.Next(1, 20),
                        OpenInterest = current.OpenInterest + _random.Next(-5, 6),
                        UpdateTime = DateTime.Now,
                    };

                    _quotes[id] = next;
                    QuoteUpdated?.Invoke(this, next);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
    }

    private IReadOnlyList<DepthLevel> BuildDepth(double nearest, double tick, int count)
    {
        var levels = new DepthLevel[count];
        for (var i = 0; i < count; i++)
        {
            levels[i] = new DepthLevel(Math.Round(nearest - i * tick, 4), _random.Next(2, 60));
        }

        return levels;
    }

    private static int StableHash(string value)
    {
        var hash = 17;
        foreach (var ch in value)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return hash & int.MaxValue;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MockMarketDataClient));
        }
    }
}
