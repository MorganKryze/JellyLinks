using Jellyfin.Plugin.JellyLinks.Data;
using JellyLinks.Tests.Fakes;
using Xunit;

namespace JellyLinks.Tests;

public class LinkStoreTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();

    [Fact]
    public void Migrate_is_idempotent()
    {
        using var t = new TempStore();
        t.Store.Migrate();
        t.Store.Migrate();
    }

    [Fact]
    public void Batch_and_links_round_trip_with_selection()
    {
        using var t = new TempStore();
        var sel = new Selection(new[] { Item }, true, false, new[] { Guid.NewGuid() }, new[] { "abc" });
        var id = t.Store.CreateBatch(User, 100, 200, "Film", sel,
            new[] { TempStore.Video(Item, "Film (2020).mkv", 1000) });

        var b = t.Store.GetBatch(id)!;
        Assert.Equal(User, b.UserId);
        Assert.Equal(BatchStates.Active, b.State);
        Assert.Equal(sel.RootItemIds, b.Selection.RootItemIds);
        Assert.Equal(sel.ExcludedMediaSourceIds, b.Selection.ExcludedMediaSourceIds);
        Assert.True(b.Selection.AllVersions);

        var link = Assert.Single(t.Store.GetLinks(id));
        Assert.Equal("Film (2020).mkv", link.FileName);
        Assert.Equal(id, link.BatchId);
    }

    [Fact]
    public void Distinct_ips_are_counted_per_batch()
    {
        using var t = new TempStore();
        var id = t.Store.CreateBatch(User, 100, 200, "x", TempStore.AnySelection(Item),
            new[] { TempStore.Video(Item, "a.mkv", 10), TempStore.Video(Guid.NewGuid(), "b.mkv", 10) });
        var links = t.Store.GetLinks(id);

        t.Store.InsertSession(new SessionRecord(0, links[0].Id, "1.1.1.1", "jd", 100, 100, 0, "", SessionStatuses.InProgress, true));
        t.Store.InsertSession(new SessionRecord(0, links[1].Id, "1.1.1.1", "jd", 100, 100, 0, "", SessionStatuses.InProgress, false));
        t.Store.InsertSession(new SessionRecord(0, links[1].Id, "2.2.2.2", "jd", 100, 100, 0, "", SessionStatuses.InProgress, true));

        Assert.Equal(2, t.Store.CountDistinctIps(id));
        Assert.True(t.Store.BatchHasIp(id, "2.2.2.2"));
        Assert.False(t.Store.BatchHasIp(id, "3.3.3.3"));
    }

    [Fact]
    public void Usage_sums_from_a_day()
    {
        using var t = new TempStore();
        t.Store.AddUsage(User, 10, 100);
        t.Store.AddUsage(User, 10, 50);
        t.Store.AddUsage(User, 12, 7);
        Assert.Equal(157, t.Store.GetUsageSince(User, 10));
        Assert.Equal(7, t.Store.GetUsageSince(User, 11));
        Assert.Equal(0, t.Store.GetUsageSince(Guid.NewGuid(), 0));
    }

    [Fact]
    public void Active_batches_exclude_expired_revoked_and_blocked()
    {
        using var t = new TempStore();
        var sel = TempStore.AnySelection(Item);
        var links = new[] { TempStore.Video(Item, "a.mkv", 10) };
        t.Store.CreateBatch(User, 0, 1000, "live", sel, links);
        t.Store.CreateBatch(User, 0, 50, "old", sel, links);
        var revoked = t.Store.CreateBatch(User, 0, 1000, "revoked", sel, links);
        t.Store.SetBatchState(revoked, BatchStates.Revoked, null);

        Assert.Equal(1, t.Store.CountActiveBatches(User, 100));
    }
}
