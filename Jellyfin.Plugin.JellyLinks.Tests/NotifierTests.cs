using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Notify;
using JellyLinks.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyLinks.Tests;

public class NotifierTests
{
    private static readonly LinkEvent Ev = new(EventKind.BatchBlocked, Guid.Empty, "julien", "Film", 1, "detail");

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
}
