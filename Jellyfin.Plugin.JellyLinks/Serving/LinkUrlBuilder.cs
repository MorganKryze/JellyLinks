namespace Jellyfin.Plugin.JellyLinks.Serving;

public static class LinkUrlBuilder
{
    /// <summary>The trailing name is decorative: only the token is checked.</summary>
    public static string Build(string baseUrl, string token, string fileName) =>
        $"{baseUrl.TrimEnd('/')}/JellyLinks/f/{token}/{Uri.EscapeDataString(fileName)}";
}
