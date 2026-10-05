using Jellyfin.Plugin.JellyLinks.I18n;

namespace Jellyfin.Plugin.JellyLinks.Notify;

public enum EventKind
{
    BatchBlocked,
    NewIp,
    QuotaReached,
    BatchCompleted,
    BatchCreated,
    BatchRevoked,
    BatchUnblocked,
    LinkMoved,
    AllRevoked,
}

public static class EventKinds
{
    /// <summary>The four events the spec sends to the activity log and the webhook; the others are journal-only.</summary>
    public static bool IsAlert(EventKind k) =>
        k is EventKind.BatchBlocked or EventKind.NewIp or EventKind.QuotaReached or EventKind.BatchCompleted;
}

public sealed record LinkEvent(EventKind Kind, Guid UserId, string UserName, string BatchLabel, long BatchId, Msg Detail);
