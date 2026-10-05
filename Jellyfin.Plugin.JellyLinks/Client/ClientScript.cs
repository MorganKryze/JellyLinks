using System.Text;

namespace Jellyfin.Plugin.JellyLinks.Client;

/// <summary>The web client script: the dictionary, then the embedded Client/*.js files in dependency order.</summary>
public static class ClientScript
{
    public static readonly string[] Parts = { "core.js", "generate.js", "mylinks.js", "boot.js" };

    /// <summary>The dictionary shared by the client, the admin page and the server's own messages (raw JSON).</summary>
    public static string Strings() => Read("strings.json");

    public static string Load() => Load(Parts);

    public static string Load(IEnumerable<string> parts)
    {
        var sb = new StringBuilder();
        sb.Append("(window.JellyLinks = window.JellyLinks || {}).STRINGS = ").Append(Strings().Trim()).AppendLine(";");
        foreach (var part in parts)
        {
            sb.AppendLine(Read(part));
        }

        return sb.ToString();
    }

    private static string Read(string name)
    {
        using var stream = typeof(ClientScript).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.JellyLinks.Client.{name}")
            ?? throw new InvalidOperationException($"embedded client part missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
