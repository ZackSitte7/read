using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Replica.Core.Abstractions;
using Replica.Core.Models;

namespace Replica.TqSdk;

/// <summary>TqSdk 桥接进程返回的错误。</summary>
public sealed class TqSdkBridgeException(string message) : Exception(message);

/// <summary>
/// 通过 Python 子进程桥接 TqSdk：同时实现行情与交易接口，
/// 一个进程内共享同一个 TqApi，避免行情、交易重复登录。
/// </summary>
public sealed class TqSdkBridgeClient : IMarketDataClient, ITradeClient
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TqSdkOptions _options;
    private readonly IReadOnlyCollection<Instrument> _instruments;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradedVolume = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _tradePrice = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly CancellationTokenSource _life = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _reader;
    private Task? _stderr;
    private TaskCompletionSource<bool>? _ready;
    private long _requestId;

    private volatile bool _connected;
    private volatile bool _disposed;
    private string _lastError = string.Empty;

    public TqSdkBridgeClient(TqSdkOptions options, IReadOnlyCollection<Instrument> instruments)
    {
        _options = options;
        _instruments = instruments;
    }

    public event EventHandler<Quote>? QuoteUpdated;

    public event EventHandler<Order>? OrderUpdated;

    public event EventHandler<Trade>? TradeReceived;

#pragma warning disable CS0067 // 持仓由上层轮询 QueryPositionsAsync 获取。
    public event EventHandler<Position>? PositionUpdated;
#pragma warning restore CS0067

    public event EventHandler<TradingAccountSnapshot>? AccountUpdated;

    public event EventHandler<ConnectionState>? ConnectionChanged;

    /// <summary>桥接进程的日志（连接进度、TqSdk 提示等）。</summary>
    public event EventHandler<string>? Log;

    public bool IsConnected => _connected;

    public bool IsMarketOnly => string.Equals(_options.TradeMode, "none", StringComparison.OrdinalIgnoreCase);

    public async Task ConnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connected)
            {
                return;
            }

            StartProcess();

            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ready = ready;
            _lastError = string.Empty;

            var body = new JsonObject
            {
                ["auth"] = new JsonObject
                {
                    ["user"] = _options.AuthUser,
                    ["password"] = _options.AuthPassword,
                },
                ["trade"] = new JsonObject
                {
                    ["mode"] = _options.TradeMode,
                    ["broker"] = _options.Broker,
                    ["account"] = string.IsNullOrWhiteSpace(_options.TradeAccount)
                        ? _options.AuthUser
                        : _options.TradeAccount,
                    ["password"] = string.IsNullOrWhiteSpace(_options.TradePassword)
                        ? _options.AuthPassword
                        : _options.TradePassword,
                },
            };

            ConnectionChanged?.Invoke(this, ConnectionState.Connecting);
            await SendAsync("connect", body, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"TqSdk 连接超时。{(_lastError.Length > 0 ? _lastError : "请检查网络与账号。")}");
            }

            _connected = true;
            ConnectionChanged?.Invoke(this, ConnectionState.Connected);
            Log?.Invoke(this, $"TqSdk 已连接（{(IsMarketOnly ? "仅行情" : "行情+交易")}）。");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            ConnectionChanged?.Invoke(this, ConnectionState.Faulted);
            Log?.Invoke(this, $"TqSdk 连接失败：{ex.Message}");
            throw;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task SubscribeAsync(
        IEnumerable<string> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        var symbols = new JsonObject();
        foreach (var instrumentId in instrumentIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            symbols[instrumentId] = MapSymbol(instrumentId);
        }

        if (symbols.Count == 0)
        {
            return;
        }

        await RequestAsync(
            "subscribe",
            body => body["symbols"] = symbols,
            TimeSpan.FromSeconds(60),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Order> PlaceOrderAsync(
        OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();
        if (IsMarketOnly)
        {
            throw new InvalidOperationException("当前为仅行情模式，未绑定交易账户。");
        }

        var ack = await RequestAsync(
            "insert_order",
            body =>
            {
                body["symbol"] = request.InstrumentId;
                body["direction"] = request.Direction == OrderDirection.Buy ? "BUY" : "SELL";
                body["offset"] = request.Offset switch
                {
                    OffsetFlag.Open => "OPEN",
                    OffsetFlag.Close => "CLOSE",
                    OffsetFlag.CloseToday => "CLOSE_TODAY",
                    // TqSdk 用 CLOSE 表示平昨（SHFE 区分平今/平昨）。
                    OffsetFlag.CloseYesterday => "CLOSE",
                    _ => "OPEN",
                };
                body["volume"] = request.Volume;
                body["price"] = request.Price;
                body["type"] = request.Type switch
                {
                    OrderType.Market => "MARKET",
                    OrderType.Fak => "FAK",
                    OrderType.Fok => "FOK",
                    _ => "LIMIT",
                };
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        var orderId = ack.TryGetProperty("order_id", out var idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;

        if (orderId.Length > 0 && _orders.TryGetValue(orderId, out var current))
        {
            return current;
        }

        return new Order
        {
            OrderId = orderId,
            InstrumentId = request.InstrumentId,
            Direction = request.Direction,
            Offset = request.Offset,
            Type = request.Type,
            Price = request.Price,
            Volume = request.Volume,
            Status = OrderStatus.Pending,
            StatusMessage = "已提交",
        };
    }

    public async Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await RequestAsync(
            "cancel_order",
            body => body["order_id"] = orderId,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Position>> QueryPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        var response = await RequestAsync(
            "query_positions",
            null,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var positions = new List<Position>();
        foreach (var item in items.EnumerateArray())
        {
            positions.Add(
                new Position
                {
                    InstrumentId = GetString(item, "symbol"),
                    Direction = string.Equals(GetString(item, "direction"), "SHORT", StringComparison.OrdinalIgnoreCase)
                        ? PositionDirection.Short
                        : PositionDirection.Long,
                    Volume = (int)GetDouble(item, "volume"),
                    AvgPrice = GetDouble(item, "avg_price"),
                    LastPrice = GetDouble(item, "last_price"),
                });
        }

        return positions;
    }

    public async Task<TradingAccountSnapshot?> QueryAccountAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        var response = await RequestAsync(
            "query_account",
            null,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        return ParseAccount(response);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_connected && _process is null)
        {
            return Task.CompletedTask;
        }

        _connected = false;
        try
        {
            if (_process is { HasExited: false })
            {
                _ = SendFireAndForgetAsync(new JsonObject { ["cmd"] = "shutdown" });
            }
        }
        catch
        {
            // 退出阶段忽略写入异常。
        }

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
        _life.Cancel();

        try
        {
            if (_process is { HasExited: false })
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                _process?.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已退出。
            }
        }

        _stdin?.Dispose();
        _process?.Dispose();
        _writeGate.Dispose();
        _connectGate.Dispose();
        _life.Dispose();
    }

    // ------------------------------------------------------------ 进程与协议

    private void StartProcess()
    {
        if (_process is not null)
        {
            return;
        }

        var script = _options.ResolveScriptPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_options.PythonPath)
                ? TqSdkOptions.DefaultPythonPath
                : _options.PythonPath,
            WorkingDirectory = Path.GetDirectoryName(script) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            StandardInputEncoding = Utf8,
        };
        startInfo.ArgumentList.Add(script);
        if (_options.Simulated)
        {
            startInfo.ArgumentList.Add("--sim");
        }

        try
        {
            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Python 桥接进程。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"启动 TqSdk 桥接进程失败：{ex.Message}\n" +
                "请安装 Python 3.10+ 与 tqsdk（pip install tqsdk），" +
                "或在登录设置中指定 Python 路径。",
                ex);
        }

        _stdin = _process.StandardInput;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => HandleProcessExit();
        _reader = Task.Run(() => ReadLoopAsync(_life.Token));
        _stderr = Task.Run(() => ReadErrorLoopAsync(_life.Token));
    }

    private async Task<long> SendAsync(string command, JsonObject body, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var payload = new JsonObject { ["id"] = id, ["cmd"] = command };
        foreach (var (key, value) in body)
        {
            payload[key] = value?.DeepClone();
        }

        await WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        return id;
    }

    private async Task SendFireAndForgetAsync(JsonObject payload)
    {
        try
        {
            await WriteAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 退出阶段忽略。
        }
    }

    private async Task<JsonElement> RequestAsync(
        string command,
        Action<JsonObject>? fill,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        fill?.Invoke(body);

        var id = Interlocked.Increment(ref _requestId);
        var payload = new JsonObject { ["id"] = id, ["cmd"] = command };
        foreach (var (key, value) in body)
        {
            payload[key] = value?.DeepClone();
        }

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var registration = timeoutSource.Token.Register(
            () => completion.TrySetException(
                new TimeoutException($"TqSdk 命令 {command} 超时。")));

        await WriteAsync(payload, cancellationToken).ConfigureAwait(false);

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task WriteAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var stdin = _stdin ?? throw new InvalidOperationException("桥接进程尚未启动。");
        var line = payload.ToJsonString();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var stdout = _process?.StandardOutput;
        if (stdout is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                HandleLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出。
        }
        catch (ObjectDisposedException)
        {
            // 正常退出。
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"读取桥接输出失败：{ex.Message}");
        }
        finally
        {
            HandleProcessExit();
        }
    }

    private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
    {
        var stderr = _process?.StandardError;
        if (stderr is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    Log?.Invoke(this, line);
                }
            }
        }
        catch
        {
            // 退出阶段忽略。
        }
    }

    private void HandleLine(string line)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // TqSdk 的第三方依赖偶尔会直接写 stdout，原样转进日志即可。
            Log?.Invoke(this, line);
            return;
        }

        var type = GetString(root, "type");
        switch (type)
        {
            case "log":
                Log?.Invoke(this, GetString(root, "message"));
                return;

            case "ready":
                _ready?.TrySetResult(true);
                return;

            case "quote":
                HandleQuote(root);
                return;

            case "order":
                HandleOrder(root);
                return;

            case "trade":
                HandleTradeMessage(root);
                return;

            case "positions":
                CompleteRequest(root);
                return;

            case "account":
                AccountUpdated?.Invoke(this, ParseAccount(root));
                CompleteRequest(root);
                return;

            case "ack":
                CompleteRequest(root);
                return;

            case "error":
                HandleError(root);
                return;

            case "fatal":
                _lastError = GetString(root, "message");
                _ready?.TrySetException(new TqSdkBridgeException(_lastError));
                Log?.Invoke(this, $"桥接进程异常：{_lastError}");
                return;

            default:
                Log?.Invoke(this, $"未知桥接消息：{line}");
                return;
        }
    }

    private void HandleError(JsonElement root)
    {
        var message = GetString(root, "message");
        if (root.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            _pending.TryRemove(idElement.GetInt64(), out var completion))
        {
            completion.TrySetException(new TqSdkBridgeException(message));
            return;
        }

        _lastError = message;
        _ready?.TrySetException(new TqSdkBridgeException(message));
        Log?.Invoke(this, $"桥接错误：{message}");
    }

    private void CompleteRequest(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            _pending.TryRemove(idElement.GetInt64(), out var completion))
        {
            completion.TrySetResult(root);
        }
    }

    private void HandleQuote(JsonElement root)
    {
        var instrumentId = GetString(root, "symbol");
        if (instrumentId.Length == 0)
        {
            return;
        }

        var quote = new Quote
        {
            InstrumentId = instrumentId,
            LastPrice = GetDouble(root, "last"),
            BidPrice = GetDouble(root, "bid"),
            BidVolume = (int)GetDouble(root, "bid_volume"),
            AskPrice = GetDouble(root, "ask"),
            AskVolume = (int)GetDouble(root, "ask_volume"),
            Bids = ParseDepth(root, "bids"),
            Asks = ParseDepth(root, "asks"),
            Volume = (int)GetDouble(root, "volume"),
            OpenInterest = GetDouble(root, "open_interest"),
            PreSettlementPrice = GetDouble(root, "pre_close"),
            UpperLimitPrice = GetDouble(root, "upper"),
            LowerLimitPrice = GetDouble(root, "lower"),
            UpdateTime = ParseTime(GetString(root, "datetime")),
        };

        QuoteUpdated?.Invoke(this, quote);
    }

    private static IReadOnlyList<DepthLevel> ParseDepth(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var levels = new List<DepthLevel>(5);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
            {
                continue;
            }

            var price = item[0].ValueKind == JsonValueKind.Number ? item[0].GetDouble() : 0;
            var volume = item[1].ValueKind == JsonValueKind.Number ? item[1].GetDouble() : 0;
            levels.Add(new DepthLevel(price, (int)volume));
        }

        return levels;
    }

    private void HandleOrder(JsonElement root)
    {
        var orderId = GetString(root, "order_id");
        if (orderId.Length == 0)
        {
            return;
        }

        var volume = (int)GetDouble(root, "volume_orign");
        var volumeLeft = (int)GetDouble(root, "volume_left");
        var traded = Math.Max(0, volume - volumeLeft);
        var statusText = GetString(root, "status");
        var status = statusText switch
        {
            "FINISHED" => traded >= volume && volume > 0 ? OrderStatus.Filled
                : traded > 0 ? OrderStatus.PartiallyFilled
                : OrderStatus.Cancelled,
            _ => traded > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Pending,
        };

        var order = new Order
        {
            OrderId = orderId,
            InstrumentId = GetString(root, "symbol"),
            Direction = string.Equals(GetString(root, "direction"), "SELL", StringComparison.OrdinalIgnoreCase)
                ? OrderDirection.Sell
                : OrderDirection.Buy,
            Offset = ParseOffset(GetString(root, "offset")),
            Type = string.Equals(GetString(root, "order_type"), "MARKET", StringComparison.OrdinalIgnoreCase)
                ? OrderType.Market
                : OrderType.Limit,
            Price = GetDouble(root, "limit_price"),
            Volume = volume,
            TradedVolume = traded,
            Status = status,
            StatusMessage = GetString(root, "last_msg"),
            UpdateTime = ParseTime(GetString(root, "update_time")),
        };

        _orders[orderId] = order;
        var tradePrice = GetDouble(root, "trade_price");
        if (tradePrice > 0)
        {
            _tradePrice[orderId] = tradePrice;
        }

        OrderUpdated?.Invoke(this, order);

        var previous = _tradedVolume.TryGetValue(orderId, out var value) ? value : 0;
        if (traded > previous)
        {
            _tradedVolume[orderId] = traded;
            TradeReceived?.Invoke(
                this,
                new Trade
                {
                    TradeId = $"{orderId}-{traded}",
                    OrderId = orderId,
                    InstrumentId = order.InstrumentId,
                    Direction = order.Direction,
                    Offset = order.Offset,
                    Price = _tradePrice.TryGetValue(orderId, out var price) ? price : order.Price,
                    Volume = traded - previous,
                    TradeTime = DateTime.Now,
                });
        }
    }

    /// <summary>桥接显式推送的成交；若已由委托变化推导过则不重复。</summary>
    private void HandleTradeMessage(JsonElement root)
    {
        var orderId = GetString(root, "order_id");
        var volume = (int)GetDouble(root, "volume");
        if (orderId.Length == 0 || volume <= 0)
        {
            return;
        }

        var previous = _tradedVolume.TryGetValue(orderId, out var value) ? value : 0;
        if (previous > 0)
        {
            return;
        }

        _tradedVolume[orderId] = volume;
        TradeReceived?.Invoke(
            this,
            new Trade
            {
                TradeId = GetString(root, "trade_id") is { Length: > 0 } tradeId
                    ? tradeId
                    : $"{orderId}-{volume}",
                OrderId = orderId,
                InstrumentId = GetString(root, "symbol"),
                Direction = string.Equals(GetString(root, "direction"), "SELL", StringComparison.OrdinalIgnoreCase)
                    ? OrderDirection.Sell
                    : OrderDirection.Buy,
                Offset = ParseOffset(GetString(root, "offset")),
                Price = GetDouble(root, "price"),
                Volume = volume,
                TradeTime = ParseTime(GetString(root, "time")),
            });
    }

    private static TradingAccountSnapshot ParseAccount(JsonElement root) =>
        new()
        {
            Balance = GetDouble(root, "balance"),
            Available = GetDouble(root, "available"),
            Margin = GetDouble(root, "margin"),
            FloatProfit = GetDouble(root, "float_profit"),
            CloseProfit = GetDouble(root, "close_profit"),
            Commission = GetDouble(root, "commission"),
            RiskRatio = GetDouble(root, "risk_ratio"),
            UpdateTime = DateTime.Now,
        };

    private void HandleProcessExit()
    {
        if (_connected || _ready is { Task.IsCompleted: false })
        {
            _lastError = _lastError.Length > 0 ? _lastError : "TqSdk 桥接进程已退出。";
            _ready?.TrySetException(new TqSdkBridgeException(_lastError));
            _connected = false;
            ConnectionChanged?.Invoke(this, ConnectionState.Disconnected);
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new TqSdkBridgeException("桥接进程已退出。"));
        }

        _pending.Clear();
    }

    private string MapSymbol(string instrumentId)
    {
        var instrument = _instruments.FirstOrDefault(
            item => string.Equals(item.InstrumentId, instrumentId, StringComparison.OrdinalIgnoreCase));
        return instrument is { ExchangeId.Length: > 0 }
            ? $"{instrument.ExchangeId}.{instrument.InstrumentId}"
            : instrumentId;
    }

    private static OffsetFlag ParseOffset(string value) => value.ToUpperInvariant() switch
    {
        "CLOSE" => OffsetFlag.Close,
        "CLOSE_TODAY" or "CLOSETODAY" => OffsetFlag.CloseToday,
        "CLOSE_YESTERDAY" or "CLOSEYESTERDAY" => OffsetFlag.CloseYesterday,
        _ => OffsetFlag.Open,
    };

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ValueKind == JsonValueKind.Number
                ? value.ToString()
                : string.Empty;

    private static double GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : 0;

    private static DateTime ParseTime(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : DateTime.Now;

    private void EnsureConnected()
    {
        if (!_connected)
        {
            throw new InvalidOperationException("TqSdk 尚未连接。");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TqSdkBridgeClient));
        }
    }
}
