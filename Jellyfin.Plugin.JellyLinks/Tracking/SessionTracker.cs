using Jellyfin.Plugin.JellyLinks.Data;

namespace Jellyfin.Plugin.JellyLinks.Tracking;

/// <summary>Groups requests into sessions (same link + same address, under 30 min apart) and keeps their status; completion is per link, across sessions.</summary>
public sealed class SessionTracker
{
    public const long IdleSeconds = 1800;

    // one lock for all sessions; per-session locks if flush contention ever shows up
    private readonly object _gate = new();
    private readonly LinkStore _store;
    private readonly TimeProvider _clock;

    public SessionTracker(LinkStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    public SessionRecord Begin(LinkRecord link, string ip, string userAgent, bool isNewIpForBatch)
    {
        lock (_gate)
        {
            var now = Now;
            var last = _store.GetLatestSession(link.Id, ip);
            if (last is not null && now - last.LastAt < IdleSeconds)
            {
                return last;
            }

            var fresh = new SessionRecord(0, link.Id, ip, userAgent, now, now, 0, string.Empty, SessionStatuses.InProgress, isNewIpForBatch);
            return fresh with { Id = _store.InsertSession(fresh) };
        }
    }

    public bool Record(ref SessionRecord session, LinkRecord link, Guid userId, long start, long bytes)
    {
        if (bytes <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            var now = Now;
            var stored = _store.GetSession(session.Id) ?? session;
            var ranges = ByteRanges.Add(ByteRanges.Parse(stored.Ranges), start, start + bytes - 1);
            // "terminé" is decided per link, across every session (a resume from another address counts).
            var linkDone = _store.AddCoverage(link.Id, ranges, link.Size);
            var isComplete = stored.Status == SessionStatuses.Complete || linkDone || ByteRanges.Covers(ranges, link.Size);

            session = stored with
            {
                LastAt = now,
                BytesSent = stored.BytesSent + bytes,
                Ranges = ByteRanges.Serialize(ranges),
                Status = isComplete ? SessionStatuses.Complete : SessionStatuses.InProgress,
            };
            _store.UpdateSession(session);
            _store.AddUsage(userId, now / 86_400, bytes);

            return linkDone;
        }
    }
}
