using System.Globalization;

namespace Replica.Ctp;

/// <summary>
/// CTP 前置地址，形如 tcp://101.230.178.177:53305。
/// 原软件把该地址直接写在 Users.xml 的 address 节点中。
/// </summary>
public readonly record struct CtpFrontAddress(string Host, int Port)
{
    public static CtpFrontAddress Parse(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName}不能为空", parameterName);
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException(
                $"{parameterName}必须是 tcp://主机:端口 形式，当前值：{value}");
        }

        if (uri.Port is <= 0 or > 65535)
        {
            throw new FormatException($"{parameterName}端口无效：{value}");
        }

        return new CtpFrontAddress(uri.Host, uri.Port);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"tcp://{Host}:{Port}");
}
