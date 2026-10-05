using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;

namespace Jellyfin.Plugin.JellyLinks.Policy;

public readonly record struct EffectiveQuota(bool Enabled, long VolumeBytes, int PeriodDays, int MaxActiveBatches);

public readonly record struct QuotaStatus(EffectiveQuota Quota, long UsedBytes, int ActiveBatches)
{
    public bool VolumeExhausted => Quota.Enabled && Quota.VolumeBytes > 0 && UsedBytes >= Quota.VolumeBytes;

    public bool BatchesExhausted => Quota.Enabled && Quota.MaxActiveBatches > 0 && ActiveBatches >= Quota.MaxActiveBatches;
}

/// <summary>Global quota, overridden per user. Volume is counted on bytes actually served.</summary>
public sealed class QuotaService
{
    private readonly LinkStore _store;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _clock;

    public QuotaService(LinkStore store, Func<PluginConfiguration> config, TimeProvider clock)
    {
        _store = store;
        _config = config;
        _clock = clock;
    }

    public EffectiveQuota GetQuota(Guid userId)
    {
        var o = _store.GetQuotaOverride(userId);
        if (o is not null)
        {
            return new EffectiveQuota(true, o.VolumeBytes, o.PeriodDays, o.MaxActiveBatches);
        }

        var c = _config();
        return new EffectiveQuota(c.QuotaEnabled, c.QuotaVolumeBytes, c.QuotaPeriodDays, c.QuotaMaxActiveBatches);
    }

    public QuotaStatus GetStatus(Guid userId)
    {
        var q = GetQuota(userId);
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var today = now / 86_400;
        var period = Math.Max(1, q.PeriodDays);
        var used = _store.GetUsageSince(userId, today - period + 1);
        return new QuotaStatus(q, used, _store.CountActiveBatches(userId, now));
    }

    /// <summary>When an exhausted volume quota gets room again: the day the oldest counted day leaves the rolling window.</summary>
    public long? FreesAt(Guid userId, QuotaStatus q)
    {
        if (!q.VolumeExhausted)
        {
            return null;
        }

        var today = _clock.GetUtcNow().ToUnixTimeSeconds() / 86_400;
        var period = Math.Max(1, q.Quota.PeriodDays);
        return _store.FirstUsageDay(userId, today - period + 1) is long first ? (first + period) * 86_400L : null;
    }
}
