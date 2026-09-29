using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.JellyLinks.Data;

/// <summary>The plugin's own SQLite database. Never jellyfin.db.</summary>
public sealed class LinkStore
{
    private const int SchemaVersion = 1;
    private readonly string _connectionString;

    public LinkStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public void Migrate()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
        {
            return;
        }

        using var tx = c.BeginTransaction();
        Exec(c, """
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
            """, tx);
        Exec(c, $"PRAGMA user_version = {SchemaVersion};", tx);
        tx.Commit();
    }

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
                INSERT INTO links (batch_id, item_id, media_source_id, file_name, size, kind, stream_index)
                VALUES ($b, $i, $m, $f, $z, $k, $x);
                """, tx, ("$b", id), ("$i", l.ItemId.ToString()), ("$m", l.MediaSourceId), ("$f", l.FileName),
                ("$z", l.Size), ("$k", l.Kind), ("$x", (object?)l.StreamIndex ?? DBNull.Value));
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

    public void SetIpLimitOverride(long id, int? limit) =>
        Exec("UPDATE batches SET ip_limit_override = $l WHERE id = $id", ("$l", (object?)limit ?? DBNull.Value), ("$id", id));

    public void MarkCompletedNotified(long id) =>
        Exec("UPDATE batches SET completed_notified = 1 WHERE id = $id", ("$id", id));

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
        Convert.ToInt64(Scalar("""
            SELECT NOT EXISTS (
              SELECT 1 FROM links l WHERE l.batch_id = $b AND NOT EXISTS (
                SELECT 1 FROM sessions s WHERE s.link_id = l.id AND s.status = 'complete'));
            """, ("$b", batchId)), CultureInfo.InvariantCulture) == 1;

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
                              WHERE b.expires_at <= $n OR b.state = 'revoked');
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
        tx.Commit();
        return n;
    }

    public int PurgeUsage(long beforeDay) => Exec("DELETE FROM usage WHERE day < $d", ("$d", beforeDay));

    // --- plumbing ---------------------------------------------------------

    private const string BatchSql =
        "SELECT id, user_id, created_at, expires_at, label, selection, state, blocked_reason, ip_limit_override, completed_notified FROM batches";

    private const string LinkSql =
        "SELECT id, batch_id, item_id, media_source_id, file_name, size, kind, stream_index FROM links";

    private const string SessionSql =
        "SELECT id, link_id, ip, user_agent, first_at, last_at, bytes_sent, ranges, status, new_ip FROM sessions";

    private static BatchRecord ReadBatch(SqliteDataReader r) => new(
        r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetInt64(2), r.GetInt64(3), r.GetString(4),
        JsonSerializer.Deserialize<Selection>(r.GetString(5))!, r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetInt32(8), r.GetInt64(9) == 1);

    private static LinkRecord ReadLink(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), Guid.Parse(r.GetString(2)), r.GetString(3), r.GetString(4),
        r.GetInt64(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetInt32(7));

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
