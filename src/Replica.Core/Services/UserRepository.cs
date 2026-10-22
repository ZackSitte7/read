using System.Text;
using System.Xml.Linq;
using Replica.Core.Models;

namespace Replica.Core.Services;

/// <summary>
/// 用户配置读写器，兼容原软件 Users.xml 的字段结构。
/// 只保存期货账户、AppID 与前置地址，明确不读写 shouquan、MacAddress 等授权字段。
/// </summary>
public static class UserRepository
{
    public static IReadOnlyList<Account> Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    public static IReadOnlyList<Account> LoadOrSeed(string path)
    {
        var accounts = Load(path);
        if (accounts.Count > 0)
        {
            return accounts;
        }

        accounts = CreateSeed();
        Save(path, accounts);
        return accounts;
    }

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
                AppId = element.Element("appid")?.Value?.Trim() ?? string.Empty,
                TradeFront = element.Element("tradeaddress")?.Value?.Trim() ?? string.Empty,
                MarketFront = element.Element("address")?.Value?.Trim() ?? string.Empty,
                // 注意：不读取 <shouquan>，复刻版不存在厂商授权码概念。
            });
        }

        return accounts;
    }

    public static void Save(string path, IEnumerable<Account> accounts)
    {
        var root = new XElement("Users");
        foreach (var account in accounts)
        {
            root.Add(
                new XElement(
                    "User",
                    new XElement("title", account.DisplayName),
                    new XElement("userid", account.UserId),
                    new XElement("brokerid", account.BrokerId),
                    new XElement("appid", account.AppId),
                    new XElement("address", account.MarketFront),
                    new XElement("tradeaddress", account.TradeFront)));
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };

        using var writer = System.Xml.XmlWriter.Create(path, settings);
        document.Save(writer);
    }

    private static IReadOnlyList<Account> CreateSeed() =>
    [
        new Account
        {
            DisplayName = "模拟账户",
            BrokerId = "9999",
            UserId = "simnow_demo",
            AppId = "simnow_client_test",
            MarketFront = "mock://market",
            TradeFront = "mock://trade",
        },
        new Account
        {
            DisplayName = "实盘账户",
            BrokerId = "9999",
            UserId = "请编辑账号",
            MarketFront = "tcp://180.168.146.187:10211",
            TradeFront = "tcp://180.168.146.187:10130",
        },
    ];
}
