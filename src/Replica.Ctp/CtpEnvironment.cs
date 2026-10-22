using System.Runtime.InteropServices;

namespace Replica.Ctp;

/// <summary>
/// CTP 运行环境检查。
/// CTP 行情/交易 API 以 C++ 抽象类导出，不能直接 P/Invoke，
/// 需要一层 C ABI 原生桥接（规划中的 ReplicaCtpBridge）。
/// </summary>
internal static class CtpEnvironment
{
    public const string MarketApiLibrary = "thostmduserapi_se.dll";

    public const string TradeApiLibrary = "thosttraderapi_se.dll";

    public static void EnsureSupported(string library)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                $"CTP 适配器只能在 Windows 上运行（当前平台 {RuntimeInformation.RuntimeIdentifier}）。" +
                "在 Linux/macOS 上请勾选登录窗口中的“使用模拟行情与模拟撮合”。");
        }

        var libraryPath = Path.Combine(AppContext.BaseDirectory, library);
        if (!File.Exists(libraryPath))
        {
            throw new PlatformNotSupportedException(
                $"未找到 {library}。请把 CTP 原生库放到程序目录：{AppContext.BaseDirectory}");
        }

        throw new NotSupportedException(
            $"已定位 {libraryPath}，但复刻版尚未生成原生 CTP 桥接层 ReplicaCtpBridge。" +
            "当前版本请使用模拟行情与模拟撮合验证交易主链路。");
    }
}
