namespace Jellyfin.Plugin.JellyLinks.Notify;

public enum EventKind
{
    BatchBlocked,
    NewIp,
    QuotaReached,
    BatchCompleted,
}

public sealed record LinkEvent(EventKind Kind, Guid UserId, string UserName, string BatchLabel, long BatchId, string Detail);
