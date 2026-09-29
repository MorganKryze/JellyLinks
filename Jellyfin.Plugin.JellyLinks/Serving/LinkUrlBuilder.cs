namespace Jellyfin.Plugin.JellyLinks.Serving;

public static class LinkUrlBuilder
{
    /// <summary>The trailing name is decorative: only the token is checked.</summary>
    public static string Build(string baseUrl, string token, string fileName) =>
        $"{baseUrl.TrimEnd('/')}/JellyLinks/f/{token}/{Uri.EscapeDataString(fileName)}";

    /// <summary>The configured public address when it is an absolute http(s) URL, else the address the request came in on.</summary>
    public static string BaseUrl(string configured, string requestBase)
    {
        var c = configured.Trim();
        return Uri.TryCreate(c, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? c
            : requestBase;
    }
}
