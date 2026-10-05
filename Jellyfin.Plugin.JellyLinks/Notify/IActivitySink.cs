namespace Jellyfin.Plugin.JellyLinks.Notify;

public interface IActivitySink
{
    Task WriteAsync(LinkEvent e, string lang);
}
