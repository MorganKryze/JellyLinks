namespace Jellyfin.Plugin.JellyLinks.Data;

public static class BatchStates
{
    public const string Active = "active";
    public const string Blocked = "blocked";
    public const string Revoked = "revoked";
    public const string Expired = "expired";

    /// <summary>State as a reader should see it: past its expiry, an active batch is expired even before maintenance runs. A blocked batch stays blocked until an admin acts.</summary>
    public static string Effective(string state, long expiresAt, long now) =>
        state == Active && expiresAt <= now ? Expired : state;
}

public static class SessionStatuses
{
    public const string InProgress = "in_progress";
    public const string Complete = "complete";
    public const string Interrupted = "interrupted";
    public const string Abandoned = "abandoned";
}

/// <summary>What the user asked for: roots, modes, and what they unticked.</summary>
public sealed record Selection(
    IReadOnlyList<Guid> RootItemIds,
    bool AllVersions,
    bool IncludeSubtitles,
    IReadOnlyList<Guid> ExcludedItemIds,
    IReadOnlyList<string> ExcludedMediaSourceIds);

public sealed record BatchRecord(
    long Id, Guid UserId, long CreatedAt, long ExpiresAt, string Label, Selection Selection,
    string State, string? BlockedReason, int? IpLimitOverride, bool CompletedNotified, BatchScope? Scope = null);

public sealed record LinkRecord(
    long Id, long BatchId, Guid ItemId, string MediaSourceId, string FileName, long Size,
    string Kind, int? StreamIndex, string Title = "", string? Fallback = null, string Covered = "", bool Complete = false);

public sealed record SessionRecord(
    long Id, long LinkId, string Ip, string UserAgent, long FirstAt, long LastAt,
    long BytesSent, string Ranges, string Status, bool NewIp);

public sealed record QuotaOverride(Guid UserId, long VolumeBytes, int PeriodDays, int MaxActiveBatches);

/// <summary>Outcome of recording an address for a batch. Distinct includes this address.</summary>
public readonly record struct IpAdmission(bool IsNew, int Distinct, bool OverLimit);

public sealed record EventRow(long Id, long At, string Kind, Guid UserId, long? BatchId, string? BatchLabel, string Detail, BatchScope? BatchScope = null);

/// <summary>Journal filter. Text matches the detail or the batch label; TextUserIds are users whose name matched the text.</summary>
public sealed record EventQuery(string? Text, string? Kind, Guid? UserId, long? Since, IReadOnlyList<Guid>? TextUserIds, int Limit = 500);

public sealed record OverviewStats(int ActiveBatches, long ServedBytes7d, int CompletedFiles7d, int BlockedBatches, int FlaggedBatches);

public sealed record DayVolume(long Day, Guid UserId, long Bytes);

public sealed record TopEntry(string Key, long Bytes);

public sealed record BatchRow(long Id, Guid UserId, string Label, long CreatedAt, long ExpiresAt, string State, string? BlockedReason,
    int? IpLimitOverride, int FileCount, int CompleteCount, long TotalBytes, int DistinctIps, BatchScope? Scope = null);

/// <summary>Batch search. Text matches the label or an address; TextUserIds are users whose name matched the text.</summary>
public sealed record BatchQuery(string? Text, IReadOnlyList<Guid>? TextUserIds, string? State, Guid? UserId, long? Since, int Limit = 200);

public sealed record SessionRow(long Id, long LinkId, long BatchId, string FileName, long Size, string Ip, string UserAgent,
    long FirstAt, long LastAt, long BytesSent, long CoveredBytes, string Status, bool NewIp);

public sealed record UserTotals(Guid UserId, long Bytes, int Completed);
