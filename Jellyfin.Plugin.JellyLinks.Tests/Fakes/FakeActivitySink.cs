using Jellyfin.Plugin.JellyLinks.Notify;

namespace JellyLinks.Tests.Fakes;

public sealed class FakeActivitySink : IActivitySink
{
    public List<LinkEvent> Events { get; } = new();

    public Task WriteAsync(LinkEvent e)
    {
        lock (Events)
        {
            Events.Add(e);
        }

        return Task.CompletedTask;
    }
}
