using Jellyfin.Plugin.JellyLinks.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyLinks.Notify;

/// <summary>Activity log always, webhook when configured. Never fails the caller.</summary>
public sealed class Notifier
{
    private readonly IActivitySink _sink;
    private readonly IHttpClientFactory _http;
    private readonly Func<PluginConfiguration> _config;
    private readonly ILogger<Notifier> _log;

    public Notifier(IActivitySink sink, IHttpClientFactory http, Func<PluginConfiguration> config, ILogger<Notifier> log)
    {
        _sink = sink;
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task PublishAsync(LinkEvent e)
    {
        try
        {
            await _sink.WriteAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JellyLinks] activity log write failed for {Kind}", e.Kind);
        }

        using var req = WebhookFormatter.Build(_config(), e);
        if (req is null)
        {
            return;
        }

        try
        {
            using var client = _http.CreateClient("JellyLinks");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var res = await client.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("[JellyLinks] webhook answered {Status} for {Kind}", (int)res.StatusCode, e.Kind);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[JellyLinks] webhook failed for {Kind}", e.Kind);
        }
    }
}
