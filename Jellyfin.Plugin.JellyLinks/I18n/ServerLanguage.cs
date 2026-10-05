namespace Jellyfin.Plugin.JellyLinks.I18n;

/// <summary>The language of the server's own messages (activity log, webhook, stored labels): the dashboard's display language.</summary>
public sealed class ServerLanguage
{
    private readonly Func<string?> _culture;

    public ServerLanguage(Func<string?> culture) => _culture = culture;

    public string Current => Strings.Lang(_culture());
}
