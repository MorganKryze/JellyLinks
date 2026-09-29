using System.Text;

namespace Jellyfin.Plugin.JellyLinks.Client;

/// <summary>The web client script: the embedded Client/*.js files, in dependency order.</summary>
public static class ClientScript
{
    public static readonly string[] Parts = { "core.js", "generate.js", "mylinks.js", "boot.js" };

    public static string Load()
    {
        var assembly = typeof(ClientScript).Assembly;
        var sb = new StringBuilder();
        foreach (var part in Parts)
        {
            using var stream = assembly.GetManifestResourceStream($"Jellyfin.Plugin.JellyLinks.Client.{part}")
                ?? throw new InvalidOperationException($"embedded client part missing: {part}");
            using var reader = new StreamReader(stream);
            sb.AppendLine(reader.ReadToEnd());
        }

        return sb.ToString();
    }
}
