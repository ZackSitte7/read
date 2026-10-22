namespace Replica.Core.Services;

/// <summary>轻量 INI 读取器，兼容原软件 config.ini 的段/键结构。</summary>
public sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniFile Load(string path) => Parse(File.ReadAllText(path));

    public static IniFile Parse(string content)
    {
        var file = new IniFile();
        Dictionary<string, string>? current = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!file._sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    file._sections[name] = current;
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || current is null)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            current[key] = value;
        }

        return file;
    }

    public string? Get(string section, string key)
    {
        if (_sections.TryGetValue(section, out var values) &&
            values.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    public string Get(string section, string key, string fallback) =>
        Get(section, key) ?? fallback;

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key), out var value) ? value : fallback;

    public bool GetBool(string section, string key, bool fallback) =>
        Get(section, key) switch
        {
            "1" => true,
            "0" => false,
            _ => fallback,
        };
}
