using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Tracking;

namespace Jellyfin.Plugin.JellyLinks.Maintenance;

public sealed record MaintenanceReport(int Expired, int Interrupted, int Abandoned, int Purged);

public sealed class MaintenanceRunner
{
    private readonly LinkStore _store;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _clock;

    public MaintenanceRunner(LinkStore store, Func<PluginConfiguration> config, TimeProvider clock)
    {
        _store = store;
        _config = config;
        _clock = clock;
    }

    public MaintenanceReport Run()
    {
        var c = _config();
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var retention = Math.Max(1, c.RetentionDays);

        var expired = _store.ExpireBatches(now);
        var interrupted = _store.MarkInterrupted(now - SessionTracker.IdleSeconds);
        var abandoned = _store.MarkAbandoned(now);
        var purged = _store.AggregateAndPurge(now - (retention * 86_400L));
        _store.PurgeUsage((now / 86_400) - Math.Max(retention, c.QuotaPeriodDays));

        return new MaintenanceReport(expired, interrupted, abandoned, purged);
    }
}
