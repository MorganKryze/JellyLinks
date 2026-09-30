using System.Collections.Concurrent;
using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyLinks.Notify;

/// <summary>Journal always; the four alerts also go to the activity log and the webhook. Never fails the caller.</summary>
public sealed class Notifier
{
    public const long QuotaRenotifySeconds = 86_400;

    private readonly IActivitySink _sink;
    private readonly IHttpClientFactory _http;
    private readonly Func<PluginConfiguration> _config;
    private readonly ILogger<Notifier> _log;
    private readonly LinkStore? _journal;
    private readonly TimeProvider _clock;

    // in memory: a restart may let one extra "quota reached" alert through
    private readonly ConcurrentDictionary<Guid, long> _quotaNotified = new();

    public Notifier(IActivitySink sink, IHttpClientFactory http, Func<PluginConfiguration> config, ILogger<Notifier> log,
                    LinkStore? journal = null, TimeProvider? clock = null)
    {
        _sink = sink;
        _http = http;
        _config = config;
        _log = log;
        _journal = journal;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task PublishAsync(LinkEvent e)
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (e.Kind == EventKind.QuotaReached)
        {
            if (_quotaNotified.TryGetValue(e.UserId, out var last) && now - last < QuotaRenotifySeconds)
            {
                return;
            }

            _quotaNotified[e.UserId] = now;
        }

        try
        {
            _journal?.AddEvent(now, e.Kind.ToString(), e.UserId, e.BatchId == 0 ? null : e.BatchId, e.Detail);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JellyLinks] journal write failed for {Kind}", e.Kind);
        }

        if (!EventKinds.IsAlert(e.Kind))
        {
            return;
        }

        try
        {
            await _sink.WriteAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JellyLinks] activity log write failed for {Kind}", e.Kind);
        }

        // fire-and-forget: a slow webhook must never delay a download
        _ = SendWebhookAsync(e);
    }

    /// <summary>Sends a sample alert with the given address and format, and says what happened (the "tester" button).</summary>
    public async Task<(bool Ok, string Message)> TestWebhookAsync(string url, string format)
    {
        var probe = new PluginConfiguration { WebhookUrl = url, WebhookFormat = format, NotifyBatchBlocked = true };
        var e = new LinkEvent(EventKind.BatchBlocked, Guid.Empty, "test", "JellyLinks — essai", 0,
            "Message d'essai envoyé depuis le panneau d'administration.");
        try
        {
            using var req = WebhookFormatter.Build(probe, e);
            if (req is null)
            {
                return (false, "Aucune adresse de webhook.");
            }

            using var client = _http.CreateClient("JellyLinks");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var res = await client.SendAsync(req).ConfigureAwait(false);
            return res.IsSuccessStatusCode
                ? (true, $"Envoyé : réponse {(int)res.StatusCode}.")
                : (false, $"Le webhook a répondu {(int)res.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (false, "Échec : " + ex.Message);
        }
    }

    private async Task SendWebhookAsync(LinkEvent e)
    {
        try
        {
            using var req = WebhookFormatter.Build(_config(), e);
            if (req is null)
            {
                return;
            }

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
