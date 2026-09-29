using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Maintenance;
using JellyLinks.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JellyLinks.Tests;

public class MaintenanceRunnerTests
{
    private const long Day = 86_400;
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Expires_interrupts_then_abandons()
    {
        using var t = new TempStore();
        var now = 1_800_000_000L;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(now));
        var item = Guid.NewGuid();
        var batch = t.Store.CreateBatch(User, now - (8 * Day), now - Day, "x", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "a.mkv", 1000) });
        var link = t.Store.GetLinks(batch)[0];
        t.Store.InsertSession(new SessionRecord(0, link.Id, "1.1.1.1", "jd", now - (2 * Day), now - (2 * Day), 10, "0-9", SessionStatuses.InProgress, true));

        var r = new MaintenanceRunner(t.Store, () => new PluginConfiguration(), clock).Run();

        Assert.Equal(1, r.Expired);
        Assert.Equal(BatchStates.Expired, t.Store.GetBatch(batch)!.State);
        Assert.Equal(SessionStatuses.Abandoned, t.Store.GetLatestSession(link.Id, "1.1.1.1")!.Status);
    }

    [Fact]
    public void Old_sessions_become_ip_free_totals()
    {
        using var t = new TempStore();
        var now = 1_800_000_000L;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(now));
        var item = Guid.NewGuid();
        var batch = t.Store.CreateBatch(User, now - (120 * Day), now - (113 * Day), "x", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "a.mkv", 1000) });
        var link = t.Store.GetLinks(batch)[0];
        var old = now - (100 * Day);
        t.Store.InsertSession(new SessionRecord(0, link.Id, "1.1.1.1", "jd", old, old, 1000, "0-999", SessionStatuses.Complete, true));
        t.Store.InsertSession(new SessionRecord(0, link.Id, "2.2.2.2", "jd", old, old, 300, "0-299", SessionStatuses.Interrupted, true));

        var r = new MaintenanceRunner(t.Store, () => new PluginConfiguration(), clock).Run();
        Assert.Equal(2, r.Purged);
        Assert.Null(t.Store.GetLatestSession(link.Id, "1.1.1.1"));

        // Read totals straight from the file: no API exposes them before plan 3.
        using var c = new SqliteConnection($"Data Source={t.DbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT bytes, completed_count FROM totals";
        using var rd = cmd.ExecuteReader();
        Assert.True(rd.Read());
        Assert.Equal(1300, rd.GetInt64(0));
        Assert.Equal(1, rd.GetInt64(1));
        cmd.Dispose();
        using var ipCheck = c.CreateCommand();
        ipCheck.CommandText = "SELECT COUNT(*) FROM sessions WHERE ip IS NOT NULL";
        Assert.Equal(0L, (long)ipCheck.ExecuteScalar()!);
    }

    [Fact]
    public void Addresses_older_than_retention_are_forgotten()
    {
        using var t = new TempStore();
        var now = 1_800_000_000L;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(now));
        var item = Guid.NewGuid();
        var batch = t.Store.CreateBatch(User, now - (120 * Day), now - (113 * Day), "x", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "a.mkv", 1000) });
        t.Store.AdmitIp(batch, "1.1.1.1", 0, now - (100 * Day));
        t.Store.AdmitIp(batch, "2.2.2.2", 0, now - Day);

        new MaintenanceRunner(t.Store, () => new PluginConfiguration(), clock).Run();

        using var c = new SqliteConnection($"Data Source={t.DbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT group_concat(ip) FROM batch_ips";
        Assert.Equal("2.2.2.2", cmd.ExecuteScalar());
    }
}
