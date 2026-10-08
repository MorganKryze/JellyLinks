using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Notify;
using Xunit;

namespace JellyLinks.Tests;

public class ActivityOverviewTests
{
    private static LinkEvent Ev(EventKind kind, Msg detail) => new(kind, Guid.NewGuid(), "camille", "Dune", 1, detail);

    [Fact]
    public void Address_alerts_read_without_the_address()
    {
        var newIp = Ev(EventKind.NewIp, Msg.Of("new_ip", ("ip", "203.0.113.7"), ("n", 2), ("limit", 3)));
        var blocked = Ev(EventKind.BatchBlocked, Msg.Of("blocked", ("ip", "203.0.113.7"), ("n", 2), ("limit", 3)));

        foreach (var e in new[] { newIp, blocked })
        {
            Assert.DoesNotContain("203.0.113.7", JellyfinActivitySink.Overview(e, "en"));
            Assert.Equal("2 distinct addresses (limit 3)", JellyfinActivitySink.Overview(e, "en"));
            Assert.Equal("2 adresses distinctes (limite 3)", JellyfinActivitySink.Overview(e, "fr"));
        }
    }

    [Fact]
    public void Other_alerts_are_rendered_as_before()
    {
        var e = Ev(EventKind.BatchCreated, Msg.Of("created", ("n", 1)));
        Assert.Equal(e.Detail.Render("en", "ev."), JellyfinActivitySink.Overview(e, "en"));
    }
}
