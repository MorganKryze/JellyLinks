using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.JellyLinks.Client;

namespace Jellyfin.Plugin.JellyLinks.I18n;

/// <summary>The dictionary (Client/strings.json) read on the server side: same keys, same plural rule as the client.</summary>
public static class Strings
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, JsonElement>>> Dict = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(ClientScript.Strings())
            ?? throw new InvalidOperationException("strings.json is empty"));

    /// <summary>"fr" for any French culture, "en" for everything else: the two languages JellyLinks ships.</summary>
    public static string Lang(string? culture) =>
        culture is not null && culture.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";

    public static string T(string lang, string key, IReadOnlyDictionary<string, string>? args = null)
    {
        if (!Find(lang, key, out var value) && !Find("en", key, out value))
        {
            return key;
        }

        var text = value.ValueKind == JsonValueKind.Object ? Plural(lang, value, args) : value.GetString() ?? key;
        if (args is null)
        {
            return text;
        }

        foreach (var (name, v) in args)
        {
            text = text.Replace("{" + name + "}", v, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Base 1000 like the client: "999 B", "1.5 KB" / "1,5 Ko".</summary>
    public static string Bytes(string lang, long n)
    {
        var units = Find(lang, "units", out var u) ? u : Dict.Value["en"]["units"];
        var culture = CultureInfo.GetCultureInfo(lang == "fr" ? "fr-FR" : "en-US");
        double value = n;
        var i = 0;
        while (value >= 1000 && i < units.GetArrayLength() - 1)
        {
            value /= 1000;
            i++;
        }

        var number = i == 0 ? n.ToString(culture) : value.ToString("0.0", culture);
        return $"{number} {units[i].GetString()}";
    }

    private static bool Find(string lang, string key, out JsonElement value)
    {
        value = default;
        return Dict.Value.TryGetValue(lang, out var d) && d.TryGetValue(key, out value);
    }

    private static string Plural(string lang, JsonElement forms, IReadOnlyDictionary<string, string>? args)
    {
        var n = args is not null && args.TryGetValue("n", out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0;
        var one = lang == "fr" ? n is 0 or 1 : n == 1;
        return (one ? forms.GetProperty("one") : forms.GetProperty("other")).GetString()!;
    }
}
