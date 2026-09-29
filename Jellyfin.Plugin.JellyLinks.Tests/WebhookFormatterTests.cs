using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Notify;
using Xunit;

namespace JellyLinks.Tests;

public class WebhookFormatterTests
{
    private static readonly LinkEvent Blocked =
        new(EventKind.BatchBlocked, Guid.Empty, "julien", "Severance — Saison 1", 7, "4 adresses distinctes (limite 3)");

    [Fact]
    public void No_url_means_no_request()
    {
        Assert.Null(WebhookFormatter.Build(new PluginConfiguration(), Blocked));
    }

    [Fact]
    public void Unticked_event_means_no_request()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://ntfy.example/topic", NotifyBatchBlocked = false };
        Assert.Null(WebhookFormatter.Build(c, Blocked));
    }

    [Fact]
    public async Task Ntfy_format_is_readable_text_with_title_and_tags()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://ntfy.example/topic", WebhookFormat = "ntfy" };
        var req = WebhookFormatter.Build(c, Blocked)!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("JellyLinks : lot bloqué", req.Headers.GetValues("Title").Single());
        Assert.Contains("warning", req.Headers.GetValues("Tags").Single());
        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("julien", body);
        Assert.Contains("Severance — Saison 1", body);
    }

    [Fact]
    public async Task Json_format_carries_the_structured_event()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://hooks.example/x", WebhookFormat = "json" };
        var body = await WebhookFormatter.Build(c, Blocked)!.Content!.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"BatchBlocked\"", body);
        Assert.Contains("\"batchId\":7", body);
    }
}
