using Jellyfin.Plugin.JellyLinks.Notify;

namespace JellyLinks.Tests.Fakes;

public sealed class FakeActivitySink : IActivitySink
{
    public List<LinkEvent> Events { get; } = new();

    public List<string> Languages { get; } = new();

    public Task WriteAsync(LinkEvent e, string lang)
    {
        lock (Events)
        {
            Events.Add(e);
            Languages.Add(lang);
        }

        return Task.CompletedTask;
    }
}
