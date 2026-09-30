using Jellyfin.Plugin.JellyLinks.Data;
using JellyLinks.Tests.Fakes;
using Xunit;

namespace JellyLinks.Tests;

public class AdminQueriesTests
{
    private const long Now = 1_800_000_000;
    private const long Day = 86_400;
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static long Batch(TempStore t, Guid user, string label, long created, long expires, params long[] sizes) =>
        t.Store.CreateBatch(user, created, expires, label, TempStore.AnySelection(Guid.NewGuid()),
            sizes.Select((s, i) => TempStore.Video(Guid.NewGuid(), $"{label}-{i}.mkv", s) with { Title = label }).ToList());

    private static void Session(TempStore t, long linkId, string ip, long at, long bytes, string ranges, string status) =>
        t.Store.InsertSession(new SessionRecord(0, linkId, ip, "JDownloader", at, at, bytes, ranges, status, true));

    [Fact]
    public void Overview_counts_what_the_dashboard_shows()
    {
        using var t = new TempStore();
        var a = Batch(t, Alice, "Andor", Now - Day, Now + Day, 100, 100);
        var aLinks = t.Store.GetLinks(a);
        t.Store.AddCoverage(aLinks[0].Id, 0, 99, 100);
        Session(t, aLinks[0].Id, "1.1.1.1", Now - 10, 100, "0-99", SessionStatuses.Complete);
        t.Store.AdmitIp(a, "1.1.1.1", 0, Now);
        t.Store.AdmitIp(a, "2.2.2.2", 0, Now);
        var b = Batch(t, Bob, "Severance", Now - Day, Now + Day, 10);
        t.Store.SetBatchState(b, BatchStates.Blocked, "4 adresses distinctes (limite 3)");
        Batch(t, Alice, "Old", Now - (9 * Day), Now - 1, 10);
        t.Store.AddUsage(Alice, Now / Day, 500);
        t.Store.AddUsage(Bob, (Now / Day) - 10, 900);

        var o = t.Store.GetOverview(Now);

        Assert.Equal(new OverviewStats(1, 500, 1, 1, 1), o);
    }

    [Fact]
    public void Search_matches_label_address_and_user_literally()
    {
        using var t = new TempStore();
        var sev = Batch(t, Bob, "Severance — Saison 1", Now - 100, Now + Day, 10);
        var dune = Batch(t, Alice, "Dune 100% remaster", Now - 50, Now + Day, 10);
        t.Store.AdmitIp(dune, "203.0.113.7", 0, Now);

        Assert.Equal(new[] { sev }, t.Store.SearchBatches(new BatchQuery("sever", null, null, null, null), Now).Select(r => r.Id));
        Assert.Equal(new[] { dune }, t.Store.SearchBatches(new BatchQuery("203.0.113", null, null, null, null), Now).Select(r => r.Id));
        Assert.Equal(new[] { dune }, t.Store.SearchBatches(new BatchQuery("100%", null, null, null, null), Now).Select(r => r.Id));
        Assert.Empty(t.Store.SearchBatches(new BatchQuery("_", null, null, null, null), Now));
        Assert.Equal(new[] { sev }, t.Store.SearchBatches(new BatchQuery("julien", new[] { Bob }, null, null, null), Now).Select(r => r.Id));
        Assert.Equal(new[] { dune, sev }, t.Store.SearchBatches(new BatchQuery(null, null, null, null, null), Now).Select(r => r.Id));
    }

    [Fact]
    public void Search_filters_on_effective_state_user_and_period()
    {
        using var t = new TempStore();
        var live = Batch(t, Alice, "A", Now - 100, Now + Day, 10, 20);
        var lapsed = Batch(t, Alice, "B", Now - (10 * Day), Now - 1, 10);
        var bobs = Batch(t, Bob, "C", Now - 100, Now + Day, 10);
        t.Store.AddCoverage(t.Store.GetLinks(live)[0].Id, 0, 9, 10);
        t.Store.AdmitIp(live, "1.1.1.1", 0, Now);

        var expired = t.Store.SearchBatches(new BatchQuery(null, null, BatchStates.Expired, null, null), Now);
        Assert.Equal(new[] { lapsed }, expired.Select(r => r.Id));
        Assert.Equal(BatchStates.Expired, expired[0].State);
        Assert.Equal(new[] { bobs }, t.Store.SearchBatches(new BatchQuery(null, null, null, Bob, null), Now).Select(r => r.Id));
        Assert.DoesNotContain(lapsed, t.Store.SearchBatches(new BatchQuery(null, null, null, null, Now - Day), Now).Select(r => r.Id));

        var row = t.Store.GetBatchRow(live, Now)!;
        Assert.Equal((2, 1, 30L, 1), (row.FileCount, row.CompleteCount, row.TotalBytes, row.DistinctIps));
    }

    [Fact]
    public void Sessions_of_a_batch_show_file_address_and_covered_bytes()
    {
        using var t = new TempStore();
        var id = Batch(t, Alice, "A", Now - 100, Now + Day, 1000);
        var link = t.Store.GetLinks(id)[0];
        Session(t, link.Id, "203.0.113.7", Now - 50, 150, "0-99", SessionStatuses.Interrupted);

        var s = Assert.Single(t.Store.SessionsOfBatch(id));
        Assert.Equal(("A-0.mkv", 1000L, "203.0.113.7", 150L, 100L, id), (s.FileName, s.Size, s.Ip, s.BytesSent, s.CoveredBytes, s.BatchId));
        Assert.Single(t.Store.RecentSessions(Now - 60, 10));
        Assert.Empty(t.Store.RecentSessions(Now - 10, 10));
    }

    [Fact]
    public void Top_lists_and_daily_volumes()
    {
        using var t = new TempStore();
        var a = Batch(t, Alice, "Andor", Now - 100, Now + Day, 1000);
        var d = Batch(t, Bob, "Dune", Now - 100, Now + Day, 1000);
        Session(t, t.Store.GetLinks(a)[0].Id, "1.1.1.1", Now - 10, 300, "0-299", SessionStatuses.InProgress);
        Session(t, t.Store.GetLinks(d)[0].Id, "1.1.1.1", Now - 10, 900, "0-899", SessionStatuses.InProgress);
        t.Store.AddUsage(Alice, Now / Day, 300);
        t.Store.AddUsage(Bob, Now / Day, 900);
        t.Store.AddUsage(Bob, (Now / Day) - 40, 5);

        Assert.Equal(new[] { new TopEntry("Dune", 900), new TopEntry("Andor", 300) }, t.Store.TopTitles(Now - Day, 10));
        Assert.Equal(new[] { new TopEntry(Bob.ToString(), 900), new TopEntry(Alice.ToString(), 300) }, t.Store.TopUsers((Now / Day) - 29, 10));
        Assert.Equal(2, t.Store.DailyVolumes((Now / Day) - 29).Count);
    }

    [Fact]
    public void Archived_totals_and_revoke_all()
    {
        using var t = new TempStore();
        var a = Batch(t, Alice, "A", Now - (120 * Day), Now + Day, 1000);
        var link = t.Store.GetLinks(a)[0];
        Session(t, link.Id, "1.1.1.1", Now - (100 * Day), 1000, "0-999", SessionStatuses.Complete);
        t.Store.AggregateAndPurge(Now - (90 * Day));
        var blocked = Batch(t, Bob, "B", Now, Now + Day, 1);
        t.Store.SetBatchState(blocked, BatchStates.Blocked, "x");

        Assert.Equal(new[] { new UserTotals(Alice, 1000, 1) }, t.Store.ArchivedTotals());
        var lapsed = Batch(t, Bob, "L", Now - (10 * Day), Now - 1, 1);
        Assert.Equal(2, t.Store.RevokeAll("tout révoqué", Now));
        Assert.Equal(BatchStates.Revoked, t.Store.GetBatch(blocked)!.State);
        Assert.Equal(BatchStates.Active, t.Store.GetBatch(lapsed)!.State);
    }
}
