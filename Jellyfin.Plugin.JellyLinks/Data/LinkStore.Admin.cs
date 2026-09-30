using Jellyfin.Plugin.JellyLinks.Tracking;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.JellyLinks.Data;

/// <summary>Read side of the admin panel. States are effective: an active batch past its expiry reads as expired.</summary>
public sealed partial class LinkStore
{
    private const string EffectiveState =
        "CASE WHEN b.state = 'active' AND b.expires_at <= $now THEN 'expired' ELSE b.state END";

    private const string BatchRowSql = $"""
        SELECT b.id, b.user_id, b.label, b.created_at, b.expires_at, {EffectiveState} AS state,
               b.blocked_reason, b.ip_limit_override,
               (SELECT COUNT(*) FROM links l WHERE l.batch_id = b.id),
               (SELECT COUNT(*) FROM links l WHERE l.batch_id = b.id AND l.complete = 1),
               (SELECT COALESCE(SUM(l.size), 0) FROM links l WHERE l.batch_id = b.id),
               (SELECT COUNT(*) FROM batch_ips i WHERE i.batch_id = b.id)
        FROM batches b
        """;

    private const string SessionRowSql = """
        SELECT s.id, s.link_id, l.batch_id, l.file_name, l.size, s.ip, s.user_agent, s.first_at, s.last_at,
               s.bytes_sent, s.ranges, s.status, s.new_ip
        FROM sessions s JOIN links l ON l.id = s.link_id
        """;

    public OverviewStats GetOverview(long now) =>
        Query("""
            SELECT
              (SELECT COUNT(*) FROM batches WHERE state = 'active' AND expires_at > $now),
              (SELECT COALESCE(SUM(bytes), 0) FROM usage WHERE day >= $day7),
              (SELECT COUNT(*) FROM links l WHERE l.complete = 1 AND EXISTS (
                 SELECT 1 FROM sessions s WHERE s.link_id = l.id AND s.status = 'complete' AND s.last_at >= $since7)),
              (SELECT COUNT(*) FROM batches WHERE state = 'blocked'),
              (SELECT COUNT(*) FROM batches b WHERE b.state = 'active' AND b.expires_at > $now
                 AND (SELECT COUNT(*) FROM batch_ips i WHERE i.batch_id = b.id) >= 2);
            """,
            r => new OverviewStats(r.GetInt32(0), r.GetInt64(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)),
            ("$now", now), ("$day7", (now / 86_400) - 6), ("$since7", now - (7 * 86_400L)))[0];

    public IReadOnlyList<DayVolume> DailyVolumes(long fromDay) =>
        Query("SELECT day, user_id, bytes FROM usage WHERE day >= $d ORDER BY day, user_id",
            r => new DayVolume(r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetInt64(2)), ("$d", fromDay));

    public IReadOnlyList<TopEntry> TopTitles(long since, int limit) =>
        Query("""
            SELECT l.title, SUM(s.bytes_sent) FROM sessions s JOIN links l ON l.id = s.link_id
            WHERE s.last_at >= $t AND l.title <> '' GROUP BY l.title ORDER BY 2 DESC, 1 LIMIT $n
            """, r => new TopEntry(r.GetString(0), r.GetInt64(1)), ("$t", since), ("$n", limit));

    public IReadOnlyList<TopEntry> TopUsers(long fromDay, int limit) =>
        Query("SELECT user_id, SUM(bytes) FROM usage WHERE day >= $d GROUP BY user_id ORDER BY 2 DESC, 1 LIMIT $n",
            r => new TopEntry(r.GetString(0), r.GetInt64(1)), ("$d", fromDay), ("$n", limit));

    public IReadOnlyList<BatchRow> SearchBatches(BatchQuery q, long now) =>
        Query($"""
            SELECT * FROM ({BatchRowSql}
              WHERE ($since IS NULL OR b.created_at >= $since)
                AND ($user IS NULL OR b.user_id = $user)
                AND ($like IS NULL OR b.label LIKE $like ESCAPE '\'
                     OR b.user_id IN (SELECT value FROM json_each($uids))
                     OR EXISTS (SELECT 1 FROM batch_ips i WHERE i.batch_id = b.id AND i.ip LIKE $like ESCAPE '\')))
            WHERE ($state IS NULL OR state = $state)
            ORDER BY created_at DESC, id DESC LIMIT $limit;
            """, ReadBatchRow,
            ("$now", now), ("$since", (object?)q.Since ?? DBNull.Value), ("$user", (object?)q.UserId?.ToString() ?? DBNull.Value),
            ("$like", (object?)Like(q.Text) ?? DBNull.Value), ("$uids", Ids(q.TextUserIds)),
            ("$state", (object?)q.State ?? DBNull.Value), ("$limit", q.Limit));

    public BatchRow? GetBatchRow(long id, long now) =>
        Query(BatchRowSql + " WHERE b.id = $id", ReadBatchRow, ("$now", now), ("$id", id)).FirstOrDefault();

    public IReadOnlyList<SessionRow> SessionsOfBatch(long batchId) =>
        Query(SessionRowSql + " WHERE l.batch_id = $b ORDER BY s.first_at, s.id", ReadSessionRow, ("$b", batchId));

    public IReadOnlyList<SessionRow> RecentSessions(long since, int limit) =>
        Query(SessionRowSql + " WHERE s.last_at >= $t ORDER BY s.last_at DESC, s.id DESC LIMIT $n", ReadSessionRow,
            ("$t", since), ("$n", limit));

    public IReadOnlyList<UserTotals> ArchivedTotals() =>
        Query("SELECT user_id, SUM(bytes), SUM(completed_count) FROM totals GROUP BY user_id ORDER BY 2 DESC",
            r => new UserTotals(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetInt32(2)));

    /// <summary>Revokes every active or blocked batch (the "Tout révoquer" button, together with a new signing secret).</summary>
    public int RevokeAll(string reason, long now) =>
        Exec("UPDATE batches SET state = 'revoked', blocked_reason = $r WHERE (state = 'active' AND expires_at > $now) OR state = 'blocked'",
            ("$r", reason), ("$now", now));

    private static BatchRow ReadBatchRow(SqliteDataReader r) => new(
        r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetInt32(7),
        r.GetInt32(8), r.GetInt32(9), r.GetInt64(10), r.GetInt32(11));

    private static SessionRow ReadSessionRow(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetInt64(4), r.GetString(5), r.GetString(6),
        r.GetInt64(7), r.GetInt64(8), r.GetInt64(9), ByteRanges.CoveredBytes(r.GetString(10)), r.GetString(11), r.GetInt64(12) == 1);
}
