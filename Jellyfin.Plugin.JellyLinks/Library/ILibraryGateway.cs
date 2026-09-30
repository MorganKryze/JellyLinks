using Jellyfin.Plugin.JellyLinks.Data;

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
    string? ItemName = null,
    string? Fallback = null);

/// <summary>Everything the plugin needs from Jellyfin, behind one seam.</summary>
public interface ILibraryGateway
{
    /// <summary>Movies/episodes under the roots, one entry per video version and per external subtitle.</summary>
    IReadOnlyList<ResolvedFile> Expand(Guid userId, IReadOnlyList<Guid> rootItemIds, bool allVersions, bool includeSubtitles);

    /// <summary>
    /// Where a stored link's file is now, for this user: Forbidden when the user lacks the download permission or can no
    /// longer see the item; Found with the file (possibly a new identity after a rename); Gone otherwise, including when
    /// several replacements would fit.
    /// </summary>
    Location Locate(Guid userId, LinkRecord link);

    /// <summary>Display name of a user, for notifications.</summary>
    string UserName(Guid userId);
}

public sealed record LocatedFile(string Path, Guid ItemId, string MediaSourceId, int? StreamIndex);

public enum LocateOutcome
{
    Found,
    Forbidden,
    Gone,
}

public sealed record Location(LocateOutcome Outcome, LocatedFile? File)
{
    public static readonly Location Forbidden = new(LocateOutcome.Forbidden, null);
    public static readonly Location Gone = new(LocateOutcome.Gone, null);

    public static Location Found(LocatedFile file) => new(LocateOutcome.Found, file);
}
