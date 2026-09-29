namespace Jellyfin.Plugin.JellyLinks.Library;

/// <summary>A file a batch can point to, resolved from the library at a given moment.</summary>
public sealed record ResolvedFile(
    Guid ItemId,
    string MediaSourceId,
    string FileName,
    long Size,
    string Kind,
    int? StreamIndex,
    string Title,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? VersionName,
    bool Played,
    string? ItemName = null);

/// <summary>Everything the plugin needs from Jellyfin, behind one seam.</summary>
public interface ILibraryGateway
{
    /// <summary>False when the user is gone, lacks the download permission, or cannot see the item.</summary>
    bool CanDownload(Guid userId, Guid itemId);

    /// <summary>True while the item is still in the library, whoever asks (tells 403 from 410).</summary>
    bool ItemExists(Guid itemId);

    /// <summary>Movies/episodes under the roots, one entry per video version and per external subtitle.</summary>
    IReadOnlyList<ResolvedFile> Expand(Guid userId, IReadOnlyList<Guid> rootItemIds, bool allVersions, bool includeSubtitles);

    /// <summary>Current absolute path of a stored link, or null when the file no longer exists.</summary>
    string? ResolvePath(Guid userId, Guid itemId, string mediaSourceId, int? streamIndex);

    /// <summary>Display name of a user, for notifications.</summary>
    string UserName(Guid userId);
}
