namespace Jellyfin.Plugin.JellyLinks.Data;

public static class BatchStates
{
    public const string Active = "active";
    public const string Blocked = "blocked";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
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
    string State, string? BlockedReason, int? IpLimitOverride, bool CompletedNotified);

public sealed record LinkRecord(
    long Id, long BatchId, Guid ItemId, string MediaSourceId, string FileName, long Size,
    string Kind, int? StreamIndex);

public sealed record SessionRecord(
    long Id, long LinkId, string Ip, string UserAgent, long FirstAt, long LastAt,
    long BytesSent, string Ranges, string Status, bool NewIp);

public sealed record QuotaOverride(Guid UserId, long VolumeBytes, int PeriodDays, int MaxActiveBatches);

/// <summary>Outcome of recording an address for a batch. Distinct includes this address.</summary>
public readonly record struct IpAdmission(bool IsNew, int Distinct, bool OverLimit);
