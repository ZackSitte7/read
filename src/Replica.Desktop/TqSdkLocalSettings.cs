using System.Text.Json;

namespace Replica.Desktop;

/// <summary>
/// 本机 TqSdk 凭据。只在程序目录或源码 config 目录读取，
/// 不写入 Users.xml，也不进入 Git（config/tqsdk.local.json 已忽略）。
/// </summary>
internal sealed class TqSdkLocalSettings
{
    public string User { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string TradeMode { get; init; } = "kq";

    public string Broker { get; init; } = string.Empty;

    public string Account { get; init; } = string.Empty;

    public string TradePassword { get; init; } = string.Empty;

    public string PythonPath { get; init; } = string.Empty;

    public string Simulated { get; init; } = string.Empty;

    public bool IsSimulated => string.Equals(Simulated, "true", StringComparison.OrdinalIgnoreCase);

    public static TqSdkLocalSettings Load()
    {
        foreach (var path in Candidates())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<TqSdkLocalSettings>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings is { User.Length: > 0 })
                {
                    return settings;
                }
            }
            catch
            {
                // 配置损坏时退回空凭据，由用户在登录窗输入。
            }
        }

        return new TqSdkLocalSettings();
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "tqsdk.local.json");
        yield return Path.Combine(AppContext.BaseDirectory, "config", "tqsdk.local.json");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            yield return Path.Combine(directory.FullName, "config", "tqsdk.local.json");
            directory = directory.Parent;
        }
    }
}
