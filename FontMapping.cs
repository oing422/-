using System.Text;
using System.Text.Json;

namespace NovelpiaDownloader;

internal sealed class FontMapping
{
    private readonly Dictionary<char,char>? map;

    public FontMapping(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path));
            if (raw == null) return;
            map = raw.Where(x => x.Key.Length > 0 && x.Value.Length > 0)
                     .ToDictionary(x => x.Key[0], x => x.Value[0]);
        }
        catch { }
    }

    public string DecodeText(string text)
    {
        if (map == null) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(map.TryGetValue(c, out var r) ? r : c);
        return sb.ToString();
    }
}
