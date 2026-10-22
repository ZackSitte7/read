using System.Globalization;
using System.Xml.Linq;
using Replica.Core.Models;

namespace Replica.Core.Services;

/// <summary>合约仓库，读取原软件 Instruments.xml 结构。</summary>
public sealed class InstrumentRepository
{
    private readonly Dictionary<string, Instrument> _byId;

    public InstrumentRepository(IEnumerable<Instrument> instruments)
    {
        _byId = instruments
            .Where(i => !string.IsNullOrWhiteSpace(i.InstrumentId))
            .GroupBy(i => i.InstrumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<Instrument> All => _byId.Values;

    public Instrument? Find(string instrumentId) =>
        _byId.TryGetValue(instrumentId, out var instrument) ? instrument : null;

    public static InstrumentRepository Load(string path) =>
        Parse(File.ReadAllText(path));

    public static InstrumentRepository Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        var instruments = document.Root?
            .Elements("Instrument")
            .Select(ParseInstrument)
            .Where(i => i is not null)
            .Cast<Instrument>()
            .ToArray() ?? [];

        return new InstrumentRepository(instruments);
    }

    /// <summary>内置示例合约，保证在没有样本文件时也能运行。</summary>
    public static InstrumentRepository Default { get; } = new(
    [
        Make("rb2601", "螺纹钢2601", "SHFE", "rb", 10, 1),
        Make("IF2601", "沪深300 2601", "CFFEX", "IF", 300, 0.2, 0.12),
        Make("au2612", "沪金2612", "SHFE", "au", 1000, 0.02),
        Make("m2605", "豆粕2605", "DCE", "m", 10, 1, 0.09),
        Make("TA605", "PTA605", "CZCE", "TA", 5, 2),
        Make("rb2605", "螺纹钢2605", "SHFE", "rb", 10, 1),
        Make("IF2606", "沪深300 2606", "CFFEX", "IF", 300, 0.2, 0.12),
        Make("au2606", "沪金2606", "SHFE", "au", 1000, 0.02),
        Make("m2609", "豆粕2609", "DCE", "m", 10, 1, 0.09),
        Make("TA609", "PTA609", "CZCE", "TA", 5, 2),
        Make("cu2601", "沪铜2601", "SHFE", "cu", 5, 10),
        Make("ni2601", "沪镍2601", "SHFE", "ni", 1, 10, 0.12),
        Make("i2601", "铁矿石2601", "DCE", "i", 100, 0.5, 0.12),
        Make("MA605", "甲醇605", "CZCE", "MA", 10, 1),
        Make("SR605", "白糖605", "CZCE", "SR", 10, 1),
    ]);

    private static Instrument Make(
        string id,
        string name,
        string exchange,
        string product,
        int multiple,
        double tick,
        double margin = 0.1) => new()
        {
            InstrumentId = id,
            Name = name,
            ExchangeId = exchange,
            ProductId = product,
            VolumeMultiple = multiple,
            PriceTick = tick,
            LongMarginRatio = margin,
            ShortMarginRatio = margin,
        };

    private static Instrument? ParseInstrument(XElement element)
    {
        var id = element.Attribute("InstrumentID")?.Value?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return new Instrument
        {
            InstrumentId = id,
            Name = element.Attribute("InstrumentName")?.Value?.Trim() ?? string.Empty,
            ExchangeId = element.Attribute("ExchangeID")?.Value?.Trim() ?? string.Empty,
            ProductId = element.Attribute("ProductID")?.Value?.Trim() ?? string.Empty,
            VolumeMultiple = ParseInt(element.Attribute("VolumeMultiple")?.Value, 1),
            PriceTick = ParseDouble(element.Attribute("PriceTick")?.Value, 0.01),
            LongMarginRatio = ParseDouble(element.Attribute("LongMarginRatio")?.Value, 0.1),
            ShortMarginRatio = ParseDouble(element.Attribute("ShortMarginRatio")?.Value, 0.1),
            ExpireDate = element.Attribute("ExpireDate")?.Value?.Trim() ?? string.Empty,
        };
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
}
