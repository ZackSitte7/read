using Replica.Core.Models;
using Replica.Core.Services;

namespace Replica.Desktop;

/// <summary>登录成功后在各窗口之间共享的运行时上下文。</summary>
internal sealed class ReplicaContext
{
    public required TradingSession Session { get; init; }

    public required InstrumentRepository Instruments { get; init; }

    public required IReadOnlyList<Account> Accounts { get; init; }

    public required LogHub Log { get; init; }

    public required string UsersPath { get; init; }

    public required string ConfigPath { get; init; }

    public bool IsMock { get; init; }

    public Replica.Core.Mock.MockTradeClient? MockTrade { get; init; }
}
