using Microsoft.Extensions.Time.Testing;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Notify;
using JellyLinks.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyLinks.Tests;

public class NotifierTests
{
    private static readonly LinkEvent Ev = new(EventKind.BatchBlocked, Guid.Empty, "julien", "Film", 1, Msg.Of("blocked", ("n", 4), ("limit", 3), ("ip", "1.1.1.1")));

    private sealed class ThrowingHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Malformed_webhook_url_does_not_throw_and_sink_still_gets_the_event()
    {
        var sink = new FakeActivitySink();
        var cfg = new PluginConfiguration { WebhookUrl = "http://[bad" };
        var n = new Notifier(sink, new ThrowingHttp(), () => cfg, NullLogger<Notifier>.Instance);
        await n.PublishAsync(Ev);
        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task Failing_http_client_does_not_throw_and_sink_still_gets_the_event()
    {
        var sink = new FakeActivitySink();
        var cfg = new PluginConfiguration { WebhookUrl = "https://hooks.example/x" };
        var n = new Notifier(sink, new ThrowingHttp(), () => cfg, NullLogger<Notifier>.Instance);
        await n.PublishAsync(Ev);
        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task Every_event_is_journaled_but_only_alerts_reach_the_activity_log()
    {
        using var t = new TempStore();
        var sink = new FakeActivitySink();
        var n = new Notifier(sink, new ThrowingHttp(), () => new PluginConfiguration(), NullLogger<Notifier>.Instance,
            t.Store, new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)));

        await n.PublishAsync(new LinkEvent(EventKind.BatchCreated, Guid.NewGuid(), "camille", "Dune", 1, Msg.Of("created", ("n", 1))));
        await n.PublishAsync(new LinkEvent(EventKind.NewIp, Guid.NewGuid(), "camille", "Dune", 1, Msg.Of("new_ip", ("ip", "1.1.1.1"), ("n", 1), ("limit", 3))));

        Assert.Single(sink.Events);
        Assert.Equal(EventKind.NewIp, sink.Events[0].Kind);
        Assert.Equal(2, t.Store.ListEvents(new EventQuery(null, null, null, null, null)).Count);
    }

    [Fact]
    public async Task Quota_reached_is_announced_once_a_day_per_user()
    {
        using var t = new TempStore();
        var sink = new FakeActivitySink();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var n = new Notifier(sink, new ThrowingHttp(), () => new PluginConfiguration(), NullLogger<Notifier>.Instance, t.Store, clock);
        var user = Guid.NewGuid();
        var e = new LinkEvent(EventKind.QuotaReached, user, "camille", "Dune", 1, Msg.Of("quota", ("usedBytes", 1), ("volumeBytes", 1), ("n", 7)));

        await n.PublishAsync(e);
        clock.Advance(TimeSpan.FromHours(23));
        await n.PublishAsync(e);
        await n.PublishAsync(e with { UserId = Guid.NewGuid() });
        clock.Advance(TimeSpan.FromHours(2));
        await n.PublishAsync(e);

        Assert.Equal(3, sink.Events.Count);
        Assert.Equal(3, t.Store.ListEvents(new EventQuery(null, "QuotaReached", null, null, null)).Count);
    }

    [Fact]
    public async Task Alerts_follow_the_server_language_and_the_journal_keeps_codes()
    {
        using var t = new TempStore();
        var sink = new FakeActivitySink();
        var n = new Notifier(sink, new ThrowingHttp(), () => new PluginConfiguration(), NullLogger<Notifier>.Instance,
            t.Store, new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)), new ServerLanguage(() => "fr-FR"));

        await n.PublishAsync(Ev);

        Assert.Equal("fr", sink.Languages.Single());
        var stored = t.Store.ListEvents(new EventQuery(null, null, null, null, null)).Single().Detail;
        Assert.Equal("blocked", Msg.Parse(stored).Code);
    }
}
