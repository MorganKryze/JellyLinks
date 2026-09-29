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
    public void Addresses_are_admitted_once_per_batch_up_to_the_limit()
    {
        using var t = new TempStore();
        var id = t.Store.CreateBatch(User, 100, 200, "x", TempStore.AnySelection(Item),
            new[] { TempStore.Video(Item, "a.mkv", 10) });

        Assert.Equal(new IpAdmission(true, 1, false), t.Store.AdmitIp(id, "1.1.1.1", 2, 100));
        Assert.Equal(new IpAdmission(false, 1, false), t.Store.AdmitIp(id, "1.1.1.1", 2, 101));
        Assert.Equal(new IpAdmission(true, 2, false), t.Store.AdmitIp(id, "2.2.2.2", 2, 102));
        Assert.Equal(new IpAdmission(false, 3, true), t.Store.AdmitIp(id, "3.3.3.3", 2, 103));
        Assert.Equal(new IpAdmission(false, 2, false), t.Store.AdmitIp(id, "2.2.2.2", 2, 104));
        Assert.Equal(new IpAdmission(true, 3, false), t.Store.AdmitIp(id, "3.3.3.3", 0, 105));
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

    [Fact]
    public void Latest_session_for_link_prefers_a_complete_one()
    {
        using var t = new TempStore();
        var id = t.Store.CreateBatch(User, 100, 200, "x", TempStore.AnySelection(Item),
            new[] { TempStore.Video(Item, "a.mkv", 10) });
        var link = t.Store.GetLinks(id)[0];
        t.Store.InsertSession(new SessionRecord(0, link.Id, "1.1.1.1", "jd", 100, 100, 10, "0-9", SessionStatuses.Complete, true));
        t.Store.InsertSession(new SessionRecord(0, link.Id, "2.2.2.2", "jd", 200, 200, 1, "0-0", SessionStatuses.InProgress, true));

        Assert.Equal(SessionStatuses.Complete, t.Store.GetLatestSessionForLink(link.Id)!.Status);
    }

    [Fact]
    public void All_links_complete_needs_a_complete_session_per_link()
    {
        using var t = new TempStore();
        var id = t.Store.CreateBatch(User, 100, 200, "x", TempStore.AnySelection(Item),
            new[] { TempStore.Video(Item, "a.mkv", 10), TempStore.Video(Guid.NewGuid(), "b.mkv", 10) });
        var links = t.Store.GetLinks(id);

        t.Store.InsertSession(new SessionRecord(0, links[0].Id, "1.1.1.1", "jd", 100, 100, 10, "0-9", SessionStatuses.Complete, true));
        Assert.False(t.Store.AllLinksComplete(id));

        t.Store.InsertSession(new SessionRecord(0, links[1].Id, "1.1.1.1", "jd", 100, 100, 10, "0-9", SessionStatuses.Complete, false));
        Assert.True(t.Store.AllLinksComplete(id));
    }
}
