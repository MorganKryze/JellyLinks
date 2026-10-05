using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyLinks.I18n;

/// <summary>A message kept as a code and its arguments, rendered in each reader's language. Rows written by 0.2.x hold plain text (code "text").</summary>
public sealed record Msg(string Code, IReadOnlyDictionary<string, string> Args)
{
    public static Msg Of(string code, params (string Key, object Value)[] args) =>
        new(code, args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? string.Empty));

    public string Serialize() => JsonSerializer.Serialize(new Stored { Code = Code, Args = new Dictionary<string, string>(Args) });

    public static Msg Parse(string? stored)
    {
        if (!string.IsNullOrEmpty(stored) && stored[0] == '{')
        {
            try
            {
                var s = JsonSerializer.Deserialize<Stored>(stored);
                if (s?.Code is { Length: > 0 } code)
                {
                    return new Msg(code, s.Args ?? new Dictionary<string, string>());
                }
            }
            catch (JsonException)
            {
                // not ours: shown as text below
            }
        }

        return Of("text", ("text", stored ?? string.Empty));
    }

    /// <summary>In the given language. Arguments named *Bytes read as sizes; a limit of 0 reads as ∞.</summary>
    public string Render(string lang, string prefix)
    {
        if (Code == "text")
        {
            return Args.TryGetValue("text", out var text) ? text : string.Empty;
        }

        return Strings.T(lang, prefix + Code, Args.ToDictionary(kv => kv.Key, kv => Shown(lang, kv.Key, kv.Value)));
    }

    private static string Shown(string lang, string key, string value) =>
        key.EndsWith("Bytes", StringComparison.Ordinal) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)
            ? Strings.Bytes(lang, b)
            : key == "limit" && value == "0" ? "∞" : value;

    private sealed class Stored
    {
        [JsonPropertyName("c")]
        public string? Code { get; set; }

        [JsonPropertyName("a")]
        public Dictionary<string, string>? Args { get; set; }
    }
}
