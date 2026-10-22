using System.Globalization;
using System.Text;

namespace Replica.Desktop;

/// <summary>config.ini 的读写，对应原软件根目录的配置文件。</summary>
internal sealed class DesktopConfig
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static DesktopConfig Load(string path)
    {
        var config = new DesktopConfig();
        if (!File.Exists(path))
        {
            return config;
        }

        Dictionary<string, string>? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                if (!config._sections.TryGetValue(section, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    config._sections[section] = current;
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || current is null)
            {
                continue;
            }

            current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return config;
    }

    public string Get(string section, string key, string fallback = "")
    {
        if (_sections.TryGetValue(section, out var values) &&
            values.TryGetValue(key, out var value))
        {
            return value;
        }

        return fallback;
    }

    /// <summary>按文件中的顺序枚举全部配置项，供配置窗显示。</summary>
    public IEnumerable<(string Section, string Key, string Value)> Entries()
    {
        foreach (var (section, values) in _sections)
        {
            foreach (var (key, value) in values)
            {
                yield return (section, key, value);
            }
        }
    }

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public bool GetBool(string section, string key, bool fallback) =>
        Get(section, key) switch
        {
            "1" => true,
            "0" => false,
            _ => fallback,
        };

    public void Set(string section, string key, string value)
    {
        if (!_sections.TryGetValue(section, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = values;
        }

        values[key] = value;
    }

    public void Set(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

    public void Set(string section, string key, int value) =>
        Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    public void Save(string path)
    {
        var builder = new StringBuilder();
        foreach (var (section, values) in _sections)
        {
            builder.Append('[').AppendLine(section).Append(']');
            foreach (var (key, value) in values)
            {
                builder.Append(key).Append('=').AppendLine(value);
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }
}
