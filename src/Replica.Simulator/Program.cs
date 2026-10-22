using System.Text;
using Replica.Core.Mock;
using Replica.Core.Models;
using Replica.Core.Services;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("==============================================");
Console.WriteLine(" 期货交易终端复刻版 · 主链路模拟");
Console.WriteLine(" 登录 / 行情 / 下单 / 成交 / 持仓（无厂商授权）");
Console.WriteLine("==============================================");

var instruments = InstrumentRepository.Default;
TradingSession? session = null;
var trade = new MockTradeClient(id => session?.GetQuote(id));
var market = new MockMarketDataClient(instruments);
await using var trading = new TradingSession(market, trade);
session = trading;

trading.Log += (_, message) => Console.WriteLine($"[LOG ] {message}");
trading.ConnectionChanged += (_, state) =>
    Console.WriteLine($"[CONN] {state}");

var account = new Account
{
    BrokerId = "9999",
    UserId = "simnow_demo",
    DisplayName = "模拟账户",
    TradeFront = "mock://trade",
    MarketFront = "mock://market",
};

try
{
    await trading.ConnectAsync(account);
    await trading.SubscribeAsync(["rb2601", "IF2601", "au2612"]);
    await Task.Delay(1200);

    Console.WriteLine();
    Console.WriteLine("---------------- 行情快照 ----------------");
    foreach (var id in new[] { "rb2601", "IF2601", "au2612" })
    {
        var quote = trading.GetQuote(id);
        if (quote is null)
        {
            Console.WriteLine($"{id,-8} 无行情");
            continue;
        }

        Console.WriteLine(
            $"{id,-8} 最新 {quote.LastPrice,10:0.##}  " +
            $"买 {quote.BidPrice,10:0.##}×{quote.BidVolume,-4} " +
            $"卖 {quote.AskPrice,10:0.##}×{quote.AskVolume,-4} " +
            $"成交量 {quote.Volume}");
    }

    Console.WriteLine();
    Console.WriteLine("---------------- 市价开仓 ----------------");
    var openOrder = await trading.PlaceOrderAsync(new OrderRequest
    {
        InstrumentId = "rb2601",
        Direction = OrderDirection.Buy,
        Offset = OffsetFlag.Open,
        Type = OrderType.Market,
        Volume = 2,
    });
    Console.WriteLine($"委托 {openOrder.OrderId} 状态 {openOrder.Status}：{openOrder.StatusMessage}");

    Console.WriteLine();
    Console.WriteLine("---------------- 市价平仓 ----------------");
    var closeOrder = await trading.PlaceOrderAsync(new OrderRequest
    {
        InstrumentId = "rb2601",
        Direction = OrderDirection.Sell,
        Offset = OffsetFlag.Close,
        Type = OrderType.Market,
        Volume = 1,
    });
    Console.WriteLine($"委托 {closeOrder.OrderId} 状态 {closeOrder.Status}：{closeOrder.StatusMessage}");

    Console.WriteLine();
    Console.WriteLine("---------------- 限价挂单与撤单 ----------------");
    var quoteNow = trading.GetQuote("IF2601")!;
    var pending = await trading.PlaceOrderAsync(new OrderRequest
    {
        InstrumentId = "IF2601",
        Direction = OrderDirection.Buy,
        Offset = OffsetFlag.Open,
        Type = OrderType.Limit,
        Price = Math.Round(quoteNow.LastPrice * 0.9, 1),
        Volume = 1,
    });
    Console.WriteLine($"委托 {pending.OrderId} 状态 {pending.Status}（未触价，保持挂单）");

    await trading.CancelOrderAsync(pending.OrderId);
    Console.WriteLine($"委托 {pending.OrderId} 状态 {pending.Status}：{pending.StatusMessage}");

    Console.WriteLine();
    Console.WriteLine("---------------- 持仓 ----------------");
    var positions = await trading.QueryPositionsAsync();
    if (positions.Count == 0)
    {
        Console.WriteLine("无持仓");
    }
    else
    {
        foreach (var position in positions)
        {
            Console.WriteLine(position);
        }
    }

    Console.WriteLine();
    Console.WriteLine("主链路验证完成。");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"模拟失败：{ex}");
    return 1;
}
