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
        new Instrument
        {
            InstrumentId = "rb2601",
            ExchangeId = "SHFE",
            ProductId = "rb",
            VolumeMultiple = 10,
            PriceTick = 1,
            LongMarginRatio = 0.1,
            ShortMarginRatio = 0.1,
        },
        new Instrument
        {
            InstrumentId = "IF2601",
            ExchangeId = "CFFEX",
            ProductId = "IF",
            VolumeMultiple = 300,
            PriceTick = 0.2,
            LongMarginRatio = 0.12,
            ShortMarginRatio = 0.12,
        },
        new Instrument
        {
            InstrumentId = "au2612",
            ExchangeId = "SHFE",
            ProductId = "au",
            VolumeMultiple = 1000,
            PriceTick = 0.02,
            LongMarginRatio = 0.1,
            ShortMarginRatio = 0.1,
        },
        new Instrument
        {
            InstrumentId = "m2605",
            ExchangeId = "DCE",
            ProductId = "m",
            VolumeMultiple = 10,
            PriceTick = 1,
            LongMarginRatio = 0.09,
            ShortMarginRatio = 0.09,
        },
        new Instrument
        {
            InstrumentId = "TA605",
            ExchangeId = "CZCE",
            ProductId = "TA",
            VolumeMultiple = 5,
            PriceTick = 2,
            LongMarginRatio = 0.1,
            ShortMarginRatio = 0.1,
        },
    ]);

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
