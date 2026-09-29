using Jellyfin.Plugin.JellyLinks.Data;

namespace Jellyfin.Plugin.JellyLinks.Tracking;

/// <summary>Groups requests into sessions (same link + same address, under 30 min apart) and keeps their status.</summary>
public sealed class SessionTracker
{
    public const long IdleSeconds = 1800;

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
        var now = Now;
        var last = _store.GetLatestSession(link.Id, ip);
        if (last is not null && now - last.LastAt < IdleSeconds)
        {
            return last;
        }

        var fresh = new SessionRecord(0, link.Id, ip, userAgent, now, now, 0, string.Empty, SessionStatuses.InProgress, isNewIpForBatch);
        return fresh with { Id = _store.InsertSession(fresh) };
    }

    public bool Record(ref SessionRecord session, LinkRecord link, Guid userId, long start, long bytes)
    {
        if (bytes <= 0)
        {
            return false;
        }

        var now = Now;
        var ranges = ByteRanges.Add(ByteRanges.Parse(session.Ranges), start, start + bytes - 1);
        var wasComplete = session.Status == SessionStatuses.Complete;
        var isComplete = wasComplete || ByteRanges.Covers(ranges, link.Size);

        session = session with
        {
            LastAt = now,
            BytesSent = session.BytesSent + bytes,
            Ranges = ByteRanges.Serialize(ranges),
            Status = isComplete ? SessionStatuses.Complete : SessionStatuses.InProgress,
        };
        _store.UpdateSession(session);
        _store.AddUsage(userId, now / 86_400, bytes);

        return isComplete && !wasComplete;
    }
}
