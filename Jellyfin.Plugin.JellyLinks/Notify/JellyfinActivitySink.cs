using Jellyfin.Plugin.JellyLinks.I18n;
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
            ShortOverview = Overview(e, lang),
            LogSeverity = e.Kind is EventKind.BatchBlocked or EventKind.QuotaReached ? LogLevel.Warning : LogLevel.Information,
        });

    /// <summary>The activity-log text: address alerts say how many addresses were seen, never which.</summary>
    public static string Overview(LinkEvent e, string lang) =>
        e.Detail.Code is "new_ip" or "blocked"
            ? Msg.Of("ip_limit", ("n", e.Detail.Args["n"]), ("limit", e.Detail.Args["limit"])).Render(lang, "reason.")
            : e.Detail.Render(lang, "ev.");
}
