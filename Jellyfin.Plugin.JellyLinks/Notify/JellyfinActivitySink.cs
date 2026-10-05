using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyLinks.Notify;

public sealed class JellyfinActivitySink : IActivitySink
{
    private readonly IActivityManager _activity;

    public JellyfinActivitySink(IActivityManager activity) => _activity = activity;

    public Task WriteAsync(LinkEvent e, string lang) =>
        _activity.CreateAsync(new ActivityLog($"{WebhookFormatter.Title(lang, e.Kind)} — {e.BatchLabel}", "JellyLinks", e.UserId)
        {
            ShortOverview = e.Detail.Render(lang, "ev."),
            LogSeverity = e.Kind is EventKind.BatchBlocked or EventKind.QuotaReached ? LogLevel.Warning : LogLevel.Information,
        });
}
