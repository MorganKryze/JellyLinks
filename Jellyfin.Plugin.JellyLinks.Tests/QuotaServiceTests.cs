using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Policy;
using JellyLinks.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JellyLinks.Tests;

public class QuotaServiceTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.FromUnixTimeSeconds(86_400L * 100));

    [Fact]
    public void Disabled_by_default()
    {
        using var t = new TempStore();
        var q = new QuotaService(t.Store, () => new PluginConfiguration(), Clock).GetStatus(User);
        Assert.False(q.Quota.Enabled);
        Assert.False(q.VolumeExhausted);
    }

    [Fact]
    public void Rolling_period_ignores_older_days()
    {
        using var t = new TempStore();
        var cfg = new PluginConfiguration { QuotaEnabled = true, QuotaVolumeBytes = 1000, QuotaPeriodDays = 7 };
        t.Store.AddUsage(User, 100 - 7, 5000); // day 93: outside a 7-day window ending on day 100
        t.Store.AddUsage(User, 100 - 6, 600);
        t.Store.AddUsage(User, 100, 399);

        var q = new QuotaService(t.Store, () => cfg, Clock).GetStatus(User);
        Assert.Equal(999, q.UsedBytes);
        Assert.False(q.VolumeExhausted);

        t.Store.AddUsage(User, 100, 1);
        Assert.True(new QuotaService(t.Store, () => cfg, Clock).GetStatus(User).VolumeExhausted);
    }

    [Fact]
    public void Override_applies_even_when_global_is_off()
    {
        using var t = new TempStore();
        t.Store.SetQuotaOverride(new QuotaOverride(User, 10, 1, 2), User);
        var q = new QuotaService(t.Store, () => new PluginConfiguration(), Clock).GetQuota(User);
        Assert.Equal(new EffectiveQuota(true, 10, 1, 2), q);
    }

    [Fact]
    public void Zero_means_unlimited()
    {
        using var t = new TempStore();
        var cfg = new PluginConfiguration { QuotaEnabled = true, QuotaVolumeBytes = 0, QuotaMaxActiveBatches = 0 };
        t.Store.AddUsage(User, 100, 1_000_000);
        var q = new QuotaService(t.Store, () => cfg, Clock).GetStatus(User);
        Assert.False(q.VolumeExhausted);
        Assert.False(q.BatchesExhausted);
    }

    [Fact]
    public void An_exhausted_quota_says_when_space_frees_up()
    {
        using var t = new TempStore();
        var cfg = new PluginConfiguration { QuotaEnabled = true, QuotaVolumeBytes = 1000, QuotaPeriodDays = 7 };
        t.Store.AddUsage(User, 100 - 6, 0);
        t.Store.AddUsage(User, 100 - 5, 600);
        t.Store.AddUsage(User, 100, 600);
        var svc = new QuotaService(t.Store, () => cfg, Clock);

        Assert.Equal(86_400L * (100 - 5 + 7), svc.FreesAt(User, svc.GetStatus(User)));
        Assert.Null(svc.FreesAt(User, new QuotaService(t.Store, () => new PluginConfiguration(), Clock).GetStatus(User)));
    }
}
