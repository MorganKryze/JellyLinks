using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Tracking;
using JellyLinks.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JellyLinks.Tests;

public class SessionTrackerTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static (TempStore T, FakeTimeProvider Clock, SessionTracker Tracker, LinkRecord Link) Setup()
    {
        var t = new TempStore();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var item = Guid.NewGuid();
        var batch = t.Store.CreateBatch(User, 0, 2_000_000_000, "x", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "a.mkv", 1000) });
        return (t, clock, new SessionTracker(t.Store, clock), t.Store.GetLinks(batch)[0]);
    }

    [Fact]
    public void Requests_29_minutes_apart_share_a_session_31_do_not()
    {
        var (t, clock, tracker, link) = Setup();
        using var _ = t;
        var s1 = tracker.Begin(link, "1.1.1.1", "jd", true);
        tracker.Record(ref s1, link, User, 0, 100);

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(s1.Id, tracker.Begin(link, "1.1.1.1", "jd", false).Id);

        var s2 = tracker.Begin(link, "1.1.1.1", "jd", false);
        tracker.Record(ref s2, link, User, 100, 100);
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.NotEqual(s1.Id, tracker.Begin(link, "1.1.1.1", "jd", false).Id);
    }

    [Fact]
    public void Same_link_from_two_addresses_makes_two_sessions()
    {
        var (t, _, tracker, link) = Setup();
        using var __ = t;
        var a = tracker.Begin(link, "1.1.1.1", "jd", true);
        var b = tracker.Begin(link, "2.2.2.2", "jd", true);
        Assert.NotEqual(a.Id, b.Id);
        Assert.True(b.NewIp);
    }

    [Fact]
    public void Parallel_chunks_complete_the_session_once()
    {
        var (t, _, tracker, link) = Setup();
        using var __ = t;
        var s = tracker.Begin(link, "1.1.1.1", "jd", true);

        Assert.False(tracker.Record(ref s, link, User, 500, 500));
        Assert.True(tracker.Record(ref s, link, User, 0, 500));
        Assert.False(tracker.Record(ref s, link, User, 0, 10));

        var stored = t.Store.GetLatestSession(link.Id, "1.1.1.1")!;
        Assert.Equal(SessionStatuses.Complete, stored.Status);
        Assert.Equal(1010, stored.BytesSent);
    }

    [Fact]
    public void Missing_last_byte_is_not_complete_and_usage_is_counted()
    {
        var (t, clock, tracker, link) = Setup();
        using var __ = t;
        var s = tracker.Begin(link, "1.1.1.1", "jd", true);
        tracker.Record(ref s, link, User, 0, 999);

        Assert.Equal(SessionStatuses.InProgress, t.Store.GetLatestSession(link.Id, "1.1.1.1")!.Status);
        var today = clock.GetUtcNow().ToUnixTimeSeconds() / 86_400;
        Assert.Equal(999, t.Store.GetUsageSince(User, today));
    }

    [Fact]
    public void Stale_copies_from_parallel_requests_still_complete_once()
    {
        var (t, _, tracker, link) = Setup();
        using var __ = t;
        var a = tracker.Begin(link, "1.1.1.1", "jd", true);
        var b = tracker.Begin(link, "1.1.1.1", "jd", true);
        Assert.Equal(a.Id, b.Id);

        Assert.False(tracker.Record(ref a, link, User, 0, 500));
        Assert.True(tracker.Record(ref b, link, User, 500, 500));

        var stored = t.Store.GetSession(a.Id)!;
        Assert.Equal(SessionStatuses.Complete, stored.Status);
        Assert.Equal(1000, stored.BytesSent);
        Assert.False(tracker.Record(ref a, link, User, 0, 10));
    }

    [Fact]
    public void A_file_finished_from_two_addresses_is_complete_once()
    {
        var (t, clock, tracker, link) = Setup();
        using var __ = t;
        var first = tracker.Begin(link, "1.1.1.1", "jd", true);
        Assert.False(tracker.Record(ref first, link, User, 0, 600));

        clock.Advance(TimeSpan.FromMinutes(45));
        var second = tracker.Begin(link, "2.2.2.2", "jd", true);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(tracker.Record(ref second, link, User, 600, 400));
        Assert.False(tracker.Record(ref second, link, User, 0, 10));

        Assert.True(t.Store.GetLink(link.Id)!.Complete);
        Assert.Equal(SessionStatuses.Complete, t.Store.GetLatestSession(link.Id, "2.2.2.2")!.Status);
        Assert.Equal(SessionStatuses.InProgress, t.Store.GetLatestSession(link.Id, "1.1.1.1")!.Status);
    }

    [Fact]
    public void A_download_in_flight_at_upgrade_still_completes_the_link()
    {
        var (t, clock, tracker, link) = Setup();
        using var __ = t;
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        t.Store.InsertSession(new SessionRecord(0, link.Id, "1.1.1.1", "jd", now, now, 600, "0-599", SessionStatuses.InProgress, true));

        var session = tracker.Begin(link, "1.1.1.1", "jd", false);
        Assert.Equal("0-599", session.Ranges);
        Assert.True(tracker.Record(ref session, link, User, 600, 400));

        var stored = t.Store.GetLink(link.Id)!;
        Assert.True(stored.Complete);
        Assert.Equal("0-999", stored.Covered);
    }
}
