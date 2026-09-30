using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.JellyLinks.Tracking;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.JellyLinks.Data;

/// <summary>The plugin's own SQLite database. Never jellyfin.db.</summary>
public sealed partial class LinkStore
{
    private const int SchemaVersion = 2;
    private readonly string _connectionString;

    public LinkStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public void Migrate() => Migrate(SchemaVersion);

    /// <summary>Applies the missing schema steps in order, each in its own transaction.</summary>
    internal void Migrate(int target)
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        for (var v = version + 1; v <= target; v++)
        {
            using var tx = c.BeginTransaction();
            Exec(c, Steps[v - 1], tx);
            if (v == 2)
            {
                RebuildCoverage(c, tx);
            }

            Exec(c, $"PRAGMA user_version = {v};", tx);
            tx.Commit();
        }
    }

    /// <summary>v2: a link downloaded in several sessions before the upgrade is complete once all its sessions are united.</summary>
    private static void RebuildCoverage(SqliteConnection c, SqliteTransaction tx)
    {
        var union = new Dictionary<long, (IReadOnlyList<(long Start, long End)> Ranges, long Size)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT l.id, l.size, s.ranges FROM links l JOIN sessions s ON s.link_id = l.id WHERE l.complete = 0 ORDER BY l.id, s.id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                var ranges = union.TryGetValue(id, out var cur) ? cur.Ranges : Array.Empty<(long Start, long End)>();
                foreach (var (start, end) in ByteRanges.Parse(r.GetString(2)))
                {
                    ranges = ByteRanges.Add(ranges, start, end);
                }

                union[id] = (ranges, r.GetInt64(1));
            }
        }

        foreach (var (id, (ranges, size)) in union)
        {
            Exec(c, "UPDATE links SET covered = $c, complete = $k WHERE id = $id", tx,
                ("$c", ByteRanges.Serialize(ranges)), ("$k", ByteRanges.Covers(ranges, size) ? 1 : 0), ("$id", id));
        }
    }

    private static readonly string[] Steps = { V1, V2 };

    private const string V1 = """
        CREATE TABLE batches (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          user_id TEXT NOT NULL,
          created_at INTEGER NOT NULL,
          expires_at INTEGER NOT NULL,
          label TEXT NOT NULL,
          selection TEXT NOT NULL,
          state TEXT NOT NULL,
          blocked_reason TEXT,
          ip_limit_override INTEGER,
          completed_notified INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX ix_batches_user ON batches(user_id, created_at);
        CREATE TABLE links (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          batch_id INTEGER NOT NULL REFERENCES batches(id),
          item_id TEXT NOT NULL,
          media_source_id TEXT NOT NULL,
          file_name TEXT NOT NULL,
          size INTEGER NOT NULL,
          kind TEXT NOT NULL,
          stream_index INTEGER);
        CREATE INDEX ix_links_batch ON links(batch_id);
        CREATE TABLE sessions (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          link_id INTEGER NOT NULL REFERENCES links(id),
          ip TEXT NOT NULL,
          user_agent TEXT NOT NULL,
          first_at INTEGER NOT NULL,
          last_at INTEGER NOT NULL,
          bytes_sent INTEGER NOT NULL,
          ranges TEXT NOT NULL,
          status TEXT NOT NULL,
          new_ip INTEGER NOT NULL);
        CREATE INDEX ix_sessions_link_ip ON sessions(link_id, ip, last_at);
        CREATE TABLE batch_ips (
          batch_id INTEGER NOT NULL REFERENCES batches(id),
          ip TEXT NOT NULL,
          first_at INTEGER NOT NULL,
          PRIMARY KEY (batch_id, ip));
        CREATE TABLE usage (
          user_id TEXT NOT NULL,
          day INTEGER NOT NULL,
          bytes INTEGER NOT NULL,
          PRIMARY KEY (user_id, day));
        CREATE TABLE quota_overrides (
          user_id TEXT PRIMARY KEY,
          volume_bytes INTEGER NOT NULL,
          period_days INTEGER NOT NULL,
          max_active_batches INTEGER NOT NULL);
        CREATE TABLE totals (
          user_id TEXT NOT NULL,
          item_id TEXT NOT NULL,
          month TEXT NOT NULL,
          bytes INTEGER NOT NULL,
          completed_count INTEGER NOT NULL,
          PRIMARY KEY (user_id, item_id, month));
        """;

    // v2: completion per link across sessions, title for statistics, rename fallback key, event journal.
    private const string V2 = """
        ALTER TABLE links ADD COLUMN title TEXT NOT NULL DEFAULT '';
        ALTER TABLE links ADD COLUMN fallback TEXT;
        ALTER TABLE links ADD COLUMN covered TEXT NOT NULL DEFAULT '';
        ALTER TABLE links ADD COLUMN complete INTEGER NOT NULL DEFAULT 0;
        UPDATE links SET complete = 1,
          covered = (SELECT s.ranges FROM sessions s WHERE s.link_id = links.id AND s.status = 'complete' ORDER BY s.id LIMIT 1)
        WHERE EXISTS (SELECT 1 FROM sessions s WHERE s.link_id = links.id AND s.status = 'complete');
        CREATE INDEX ix_links_complete ON links(batch_id, complete);
        CREATE TABLE events (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          at INTEGER NOT NULL,
          kind TEXT NOT NULL,
          user_id TEXT NOT NULL,
          batch_id INTEGER,
          detail TEXT NOT NULL);
        CREATE INDEX ix_events_at ON events(at);
        """;

    public long CreateBatch(Guid userId, long createdAt, long expiresAt, string label, Selection selection, IReadOnlyList<LinkRecord> links)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var id = (long)Scalar(c, """
            INSERT INTO batches (user_id, created_at, expires_at, label, selection, state)
            VALUES ($u, $c, $e, $l, $s, 'active') RETURNING id;
            """, tx, ("$u", userId.ToString()), ("$c", createdAt), ("$e", expiresAt), ("$l", label),
            ("$s", JsonSerializer.Serialize(selection)))!;
        foreach (var l in links)
        {
            Exec(c, """
                INSERT INTO links (batch_id, item_id, media_source_id, file_name, size, kind, stream_index, title, fallback)
                VALUES ($b, $i, $m, $f, $z, $k, $x, $t, $fb);
                """, tx, ("$b", id), ("$i", l.ItemId.ToString()), ("$m", l.MediaSourceId), ("$f", l.FileName),
                ("$z", l.Size), ("$k", l.Kind), ("$x", (object?)l.StreamIndex ?? DBNull.Value),
                ("$t", l.Title), ("$fb", (object?)l.Fallback ?? DBNull.Value));
        }

        tx.Commit();
        return id;
    }

    public BatchRecord? GetBatch(long id) =>
        Query(BatchSql + " WHERE id = $id", ReadBatch, ("$id", id)).FirstOrDefault();

    public IReadOnlyList<BatchRecord> GetBatchesForUser(Guid userId) =>
        Query(BatchSql + " WHERE user_id = $u ORDER BY created_at DESC, id DESC", ReadBatch, ("$u", userId.ToString()));

    public void SetBatchState(long id, string state, string? reason) =>
        Exec("UPDATE batches SET state = $s, blocked_reason = $r WHERE id = $id",
            ("$s", state), ("$r", (object?)reason ?? DBNull.Value), ("$id", id));

    /// <summary>Compare-and-set: true only for the caller that turns a blocked batch back to active.</summary>
    public bool Unblock(long id) =>
        Exec("UPDATE batches SET state = 'active', blocked_reason = NULL WHERE id = $id AND state = 'blocked'", ("$id", id)) == 1;

    public void SetIpLimitOverride(long id, int? limit) =>
        Exec("UPDATE batches SET ip_limit_override = $l WHERE id = $id", ("$l", (object?)limit ?? DBNull.Value), ("$id", id));

    /// <summary>Compare-and-set: true for the one caller that flips the flag.</summary>
    public bool MarkCompletedNotified(long id) =>
        Exec("UPDATE batches SET completed_notified = 1 WHERE id = $id AND completed_notified = 0", ("$id", id)) == 1;

    /// <summary>
    /// Points a link at its file found again after a rename. Coverage restarts unless the file was already fully received:
    /// the link's and its sessions' ranges, since a resumed session would otherwise merge the old file's ranges back.
    /// </summary>
    public void Relink(long linkId, Guid itemId, string mediaSourceId, int? streamIndex, long size)
    {
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        Exec(c, "UPDATE sessions SET ranges = '' WHERE link_id = $id AND (SELECT complete FROM links WHERE id = $id) = 0;", tx,
            ("$id", linkId));
        Exec(c, """
            UPDATE links SET item_id = $i, media_source_id = $m, stream_index = $x, size = $z,
              covered = CASE WHEN complete = 1 THEN covered ELSE '' END
            WHERE id = $id;
            """, tx, ("$i", itemId.ToString()), ("$m", mediaSourceId), ("$x", (object?)streamIndex ?? DBNull.Value), ("$z", size), ("$id", linkId));
        tx.Commit();
    }

    public LinkRecord? GetLink(long id) =>
        Query(LinkSql + " WHERE id = $id", ReadLink, ("$id", id)).FirstOrDefault();

    public IReadOnlyList<LinkRecord> GetLinks(long batchId) =>
        Query(LinkSql + " WHERE batch_id = $b ORDER BY id", ReadLink, ("$b", batchId));

    public SessionRecord? GetSession(long id) =>
        Query(SessionSql + " WHERE id = $id", ReadSession, ("$id", id)).FirstOrDefault();

    public SessionRecord? GetLatestSession(long linkId, string ip) =>
        Query(SessionSql + " WHERE link_id = $l AND ip = $ip ORDER BY last_at DESC, id DESC LIMIT 1", ReadSession,
            ("$l", linkId), ("$ip", ip)).FirstOrDefault();

    public SessionRecord? GetLatestSessionForLink(long linkId) =>
        Query(SessionSql + " WHERE link_id = $l ORDER BY (status = 'complete') DESC, last_at DESC LIMIT 1", ReadSession,
            ("$l", linkId)).FirstOrDefault();

    public long InsertSession(SessionRecord s)
    {
        using var c = Open();
        return (long)Scalar(c, """
            INSERT INTO sessions (link_id, ip, user_agent, first_at, last_at, bytes_sent, ranges, status, new_ip)
            VALUES ($l, $ip, $ua, $f, $la, $b, $r, $s, $n) RETURNING id;
            """, null, ("$l", s.LinkId), ("$ip", s.Ip), ("$ua", s.UserAgent), ("$f", s.FirstAt), ("$la", s.LastAt),
            ("$b", s.BytesSent), ("$r", s.Ranges), ("$s", s.Status), ("$n", s.NewIp ? 1 : 0))!;
    }

    public void UpdateSession(SessionRecord s) =>
        Exec("UPDATE sessions SET last_at = $la, bytes_sent = $b, ranges = $r, status = $s WHERE id = $id",
            ("$la", s.LastAt), ("$b", s.BytesSent), ("$r", s.Ranges), ("$s", s.Status), ("$id", s.Id));

    /// <summary>
    /// Records an address for a batch, atomically: known → nothing; new and within the limit → recorded;
    /// new past the limit (when limit &gt; 0) → nothing recorded, OverLimit. Distinct counts this address.
    /// </summary>
    public IpAdmission AdmitIp(long batchId, string ip, int limit, long now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false); // BEGIN IMMEDIATE: one writer decides at a time
        var known = Convert.ToInt64(Scalar(c, "SELECT EXISTS (SELECT 1 FROM batch_ips WHERE batch_id = $b AND ip = $ip);", tx,
            ("$b", batchId), ("$ip", ip)), CultureInfo.InvariantCulture) == 1;
        var count = Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM batch_ips WHERE batch_id = $b;", tx, ("$b", batchId)),
            CultureInfo.InvariantCulture);
        if (known)
        {
            return new IpAdmission(false, count, false);
        }

        if (limit > 0 && count + 1 > limit)
        {
            return new IpAdmission(false, count + 1, true);
        }

        Exec(c, "INSERT INTO batch_ips (batch_id, ip, first_at) VALUES ($b, $ip, $t);", tx, ("$b", batchId), ("$ip", ip), ("$t", now));
        tx.Commit();
        return new IpAdmission(true, count + 1, false);
    }

    public bool AllLinksComplete(long batchId) =>
        Convert.ToInt64(Scalar("SELECT NOT EXISTS (SELECT 1 FROM links WHERE batch_id = $b AND complete = 0);", ("$b", batchId)),
            CultureInfo.InvariantCulture) == 1;

    /// <summary>Merges a served span into the link's coverage, all sessions together. True once: when the link becomes complete.</summary>
    public bool AddCoverage(long linkId, long start, long end, long size) =>
        AddCoverage(linkId, new[] { (start, end) }, size);

    /// <summary>Same, for several spans at once (a session's whole range set, so the link never lags behind its sessions).</summary>
    public bool AddCoverage(long linkId, IReadOnlyList<(long Start, long End)> spans, long size)
    {
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        string covered;
        bool was;
        using (var read = Command(c, "SELECT covered, complete FROM links WHERE id = $id", tx, new (string Name, object Value)[] { ("$id", linkId) }))
        using (var r = read.ExecuteReader())
        {
            if (!r.Read())
            {
                return false;
            }

            covered = r.GetString(0);
            was = r.GetInt64(1) == 1;
        }

        var ranges = ByteRanges.Parse(covered);
        foreach (var (start, end) in spans)
        {
            ranges = ByteRanges.Add(ranges, start, end);
        }

        var now = was || ByteRanges.Covers(ranges, size);
        Exec(c, "UPDATE links SET covered = $c, complete = $k WHERE id = $id", tx,
            ("$c", ByteRanges.Serialize(ranges)), ("$k", now ? 1 : 0), ("$id", linkId));
        tx.Commit();
        return now && !was;
    }

    public void AddUsage(Guid userId, long day, long bytes) =>
        Exec("""
            INSERT INTO usage (user_id, day, bytes) VALUES ($u, $d, $b)
            ON CONFLICT (user_id, day) DO UPDATE SET bytes = bytes + excluded.bytes;
            """, ("$u", userId.ToString()), ("$d", day), ("$b", bytes));

    public long GetUsageSince(Guid userId, long fromDay) =>
        Convert.ToInt64(Scalar("SELECT COALESCE(SUM(bytes), 0) FROM usage WHERE user_id = $u AND day >= $d;",
            ("$u", userId.ToString()), ("$d", fromDay)), CultureInfo.InvariantCulture);

    public int CountActiveBatches(Guid userId, long now) =>
        Convert.ToInt32(Scalar("SELECT COUNT(*) FROM batches WHERE user_id = $u AND state = 'active' AND expires_at > $n;",
            ("$u", userId.ToString()), ("$n", now)), CultureInfo.InvariantCulture);

    public QuotaOverride? GetQuotaOverride(Guid userId) =>
        Query("SELECT user_id, volume_bytes, period_days, max_active_batches FROM quota_overrides WHERE user_id = $u",
            r => new QuotaOverride(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetInt32(2), r.GetInt32(3)),
            ("$u", userId.ToString())).FirstOrDefault();

    public void SetQuotaOverride(QuotaOverride? value, Guid userId)
    {
        if (value is null)
        {
            Exec("DELETE FROM quota_overrides WHERE user_id = $u", ("$u", userId.ToString()));
            return;
        }

        Exec("""
            INSERT INTO quota_overrides (user_id, volume_bytes, period_days, max_active_batches) VALUES ($u, $v, $p, $m)
            ON CONFLICT (user_id) DO UPDATE SET volume_bytes = $v, period_days = $p, max_active_batches = $m;
            """, ("$u", userId.ToString()), ("$v", value.VolumeBytes), ("$p", value.PeriodDays), ("$m", value.MaxActiveBatches));
    }

    public int ExpireBatches(long now) =>
        Exec("UPDATE batches SET state = 'expired' WHERE state IN ('active', 'blocked') AND expires_at <= $n", ("$n", now));

    public int MarkInterrupted(long idleBefore) =>
        Exec("UPDATE sessions SET status = 'interrupted' WHERE status = 'in_progress' AND last_at < $t", ("$t", idleBefore));

    public int MarkAbandoned(long now) =>
        Exec("""
            UPDATE sessions SET status = 'abandoned'
            WHERE status IN ('in_progress', 'interrupted')
              AND link_id IN (SELECT l.id FROM links l JOIN batches b ON b.id = l.batch_id
                              WHERE (b.expires_at <= $n OR b.state = 'revoked') AND l.complete = 0);
            """, ("$n", now));

    /// <summary>Folds sessions older than the cut-off into IP-free monthly totals, then deletes them and old batch addresses.</summary>
    public int AggregateAndPurge(long before)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, """
            INSERT INTO totals (user_id, item_id, month, bytes, completed_count)
            SELECT b.user_id, l.item_id, strftime('%Y-%m', s.first_at, 'unixepoch'),
                   SUM(s.bytes_sent), SUM(s.status = 'complete')
            FROM sessions s JOIN links l ON l.id = s.link_id JOIN batches b ON b.id = l.batch_id
            WHERE s.last_at < $t
            GROUP BY 1, 2, 3
            ON CONFLICT (user_id, item_id, month) DO UPDATE SET
              bytes = bytes + excluded.bytes, completed_count = completed_count + excluded.completed_count;
            """, tx, ("$t", before));
        var n = Exec(c, "DELETE FROM sessions WHERE last_at < $t", tx, ("$t", before));
        Exec(c, "DELETE FROM batch_ips WHERE first_at < $t", tx, ("$t", before));
        Exec(c, "DELETE FROM events WHERE at < $t", tx, ("$t", before)); // details carry addresses: same retention
        tx.Commit();
        return n;
    }

    public void AddEvent(long at, string kind, Guid userId, long? batchId, string detail) =>
        Exec("INSERT INTO events (at, kind, user_id, batch_id, detail) VALUES ($a, $k, $u, $b, $d)",
            ("$a", at), ("$k", kind), ("$u", userId.ToString()), ("$b", (object?)batchId ?? DBNull.Value), ("$d", detail));

    public IReadOnlyList<EventRow> ListEvents(EventQuery q) =>
        Query("""
            SELECT e.id, e.at, e.kind, e.user_id, e.batch_id, b.label, e.detail
            FROM events e LEFT JOIN batches b ON b.id = e.batch_id
            WHERE ($kind IS NULL OR e.kind = $kind)
              AND ($user IS NULL OR e.user_id = $user)
              AND ($since IS NULL OR e.at >= $since)
              AND ($like IS NULL OR e.detail LIKE $like ESCAPE '\' OR b.label LIKE $like ESCAPE '\'
                   OR e.user_id IN (SELECT value FROM json_each($uids)))
            ORDER BY e.at DESC, e.id DESC LIMIT $limit;
            """,
            r => new EventRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), Guid.Parse(r.GetString(3)),
                r.IsDBNull(4) ? null : r.GetInt64(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6)),
            ("$kind", (object?)q.Kind ?? DBNull.Value), ("$user", (object?)q.UserId?.ToString() ?? DBNull.Value),
            ("$since", (object?)q.Since ?? DBNull.Value), ("$like", (object?)Like(q.Text) ?? DBNull.Value),
            ("$uids", Ids(q.TextUserIds)), ("$limit", q.Limit));

    public int MaxOverridePeriodDays() =>
        Convert.ToInt32(Scalar("SELECT COALESCE(MAX(period_days), 0) FROM quota_overrides;"), CultureInfo.InvariantCulture);

    /// <summary>"%text%" with LIKE wildcards escaped, or null for an empty search.</summary>
    internal static string? Like(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    internal static string Ids(IReadOnlyList<Guid>? ids) =>
        JsonSerializer.Serialize((ids ?? Array.Empty<Guid>()).Select(i => i.ToString()));

    public int PurgeUsage(long beforeDay) => Exec("DELETE FROM usage WHERE day < $d", ("$d", beforeDay));

    // --- plumbing ---------------------------------------------------------

    private const string BatchSql =
        "SELECT id, user_id, created_at, expires_at, label, selection, state, blocked_reason, ip_limit_override, completed_notified FROM batches";

    private const string LinkSql =
        "SELECT id, batch_id, item_id, media_source_id, file_name, size, kind, stream_index, title, fallback, covered, complete FROM links";

    private const string SessionSql =
        "SELECT id, link_id, ip, user_agent, first_at, last_at, bytes_sent, ranges, status, new_ip FROM sessions";

    private static BatchRecord ReadBatch(SqliteDataReader r) => new(
        r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetInt64(2), r.GetInt64(3), r.GetString(4),
        JsonSerializer.Deserialize<Selection>(r.GetString(5))!, r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetInt32(8), r.GetInt64(9) == 1);

    private static LinkRecord ReadLink(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), Guid.Parse(r.GetString(2)), r.GetString(3), r.GetString(4),
        r.GetInt64(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetInt32(7),
        r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetString(10), r.GetInt64(11) == 1);

    private static SessionRecord ReadSession(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetInt64(5),
        r.GetInt64(6), r.GetString(7), r.GetString(8), r.GetInt64(9) == 1);

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private int Exec(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        return Exec(c, sql, null, args);
    }

    private static int Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object Value)[] args)
    {
        using var cmd = Command(c, sql, tx, args);
        return cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        return Scalar(c, sql, null, args);
    }

    private static object? Scalar(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object Value)[] args)
    {
        using var cmd = Command(c, sql, tx, args);
        return cmd.ExecuteScalar();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql, null, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read())
        {
            list.Add(read(r));
        }

        return list;
    }

    private static SqliteCommand Command(SqliteConnection c, string sql, SqliteTransaction? tx, (string Name, object Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return cmd;
    }
}
