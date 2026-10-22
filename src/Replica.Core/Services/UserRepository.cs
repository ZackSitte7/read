using System.Xml.Linq;
using Replica.Core.Models;

namespace Replica.Core.Services;

/// <summary>
/// 用户配置读取器，兼容原软件 Users.xml。
/// 只读取账户与行情地址，明确忽略 shouquan、MacAddress 等授权字段。
/// </summary>
public static class UserRepository
{
    public static IReadOnlyList<Account> Load(string path) =>
        Parse(File.ReadAllText(path));

    public static IReadOnlyList<Account> Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        var accounts = new List<Account>();

        foreach (var element in document.Root?.Elements("User") ?? [])
        {
            var userId = element.Element("userid")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                userId = element.Element("title")?.Value?.Trim();
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                continue;
            }

            accounts.Add(new Account
            {
                BrokerId = element.Element("brokerid")?.Value?.Trim() ?? string.Empty,
                UserId = userId,
                DisplayName = element.Element("title")?.Value?.Trim() ?? userId,
                MarketFront = element.Element("address")?.Value?.Trim() ?? string.Empty,
                // 注意：不读取 <shouquan>，复刻版不存在厂商授权码概念。
            });
        }

        return accounts;
    }
}
