namespace Replica.TqSdk;

/// <summary>TqSdk 桥接进程的运行参数。密码只保存在本机配置，不进入 Git。</summary>
public sealed record TqSdkOptions
{
    /// <summary>Python 解释器；留空时按平台自动选择。</summary>
    public string PythonPath { get; init; } = string.Empty;

    /// <summary>tqsdk_bridge.py 路径；留空时在程序目录与源码目录中自动查找。</summary>
    public string ScriptPath { get; init; } = string.Empty;

    /// <summary>使用内置模拟引擎（不需要安装 tqsdk，也不连接网络）。</summary>
    public bool Simulated { get; init; }

    /// <summary>缺少 tqsdk 时自动执行 pip 安装并重试。</summary>
    public bool AutoInstall { get; init; } = true;

    /// <summary>天勤账户。</summary>
    public required string AuthUser { get; init; }

    public required string AuthPassword { get; init; }

    /// <summary>交易模式：none（仅行情）、kq（快期模拟）、account（实盘账户）。</summary>
    public string TradeMode { get; init; } = "kq";

    public string Broker { get; init; } = string.Empty;

    public string TradeAccount { get; init; } = string.Empty;

    public string TradePassword { get; init; } = string.Empty;

    /// <summary>
    /// 优先使用随程序打包的 Python 运行时（python\python.exe），
    /// 其次使用环境变量 REPLICA_PYTHON，最后回退到系统 Python。
    /// </summary>
    public static string DefaultPythonPath
    {
        get
        {
            if (Environment.GetEnvironmentVariable("REPLICA_PYTHON") is { Length: > 0 } configured &&
                File.Exists(configured))
            {
                return configured;
            }

            var bundled = Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
            if (File.Exists(bundled))
            {
                return bundled;
            }

            return OperatingSystem.IsWindows() ? "python" : "python3";
        }
    }

    /// <summary>从程序目录向上查找 bridge/tqsdk_bridge.py。</summary>
    public string ResolveScriptPath()
    {
        if (!string.IsNullOrWhiteSpace(ScriptPath) && File.Exists(ScriptPath))
        {
            return ScriptPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "bridge", "tqsdk_bridge.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "未找到 bridge/tqsdk_bridge.py，请确认发布目录包含 bridge 子目录，" +
            "或在设置中指定脚本路径。");
    }
}
