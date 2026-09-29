using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyLinks.Library;

public sealed class JellyfinLibraryGateway : ILibraryGateway
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IMediaSourceManager _sources;
    private readonly IUserDataManager _userData;

    public JellyfinLibraryGateway(ILibraryManager library, IUserManager users, IMediaSourceManager sources, IUserDataManager userData)
    {
        _library = library;
        _users = users;
        _sources = sources;
        _userData = userData;
    }

    public bool CanDownload(Guid userId, Guid itemId)
    {
        var user = _users.GetUserById(userId);
        return user is not null
            && user.HasPermission(PermissionKind.EnableContentDownloading)
            && Visible(itemId, user) is not null;
    }

    public IReadOnlyList<ResolvedFile> Expand(Guid userId, IReadOnlyList<Guid> rootItemIds, bool allVersions, bool includeSubtitles)
    {
        var user = _users.GetUserById(userId);
        if (user is null || !user.HasPermission(PermissionKind.EnableContentDownloading))
        {
            return Array.Empty<ResolvedFile>();
        }

        var files = new List<ResolvedFile>();
        var seen = new HashSet<Guid>();
        foreach (var root in rootItemIds)
        {
            foreach (var video in Videos(user, root))
            {
                if (seen.Add(video.Id))
                {
                    files.AddRange(FilesOf(user, video, allVersions, includeSubtitles));
                }
            }
        }

        return files;
    }

    public string? ResolvePath(Guid userId, Guid itemId, string mediaSourceId, int? streamIndex)
    {
        var user = _users.GetUserById(userId);
        var item = user is null ? null : Visible(itemId, user);
        if (user is null || item is null)
        {
            return null;
        }

        var source = _sources.GetStaticMediaSources(item, false, user)
            .FirstOrDefault(s => string.Equals(s.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return null;
        }

        if (streamIndex is null)
        {
            return source.Path;
        }

        return source.MediaStreams
            .FirstOrDefault(m => m.Index == streamIndex && m.Type == MediaStreamType.Subtitle && m.IsExternal)?.Path;
    }

    public string UserName(Guid userId) => _users.GetUserById(userId)?.Username ?? userId.ToString("N");

    private IEnumerable<BaseItem> Videos(User user, Guid rootId)
    {
        var root = Visible(rootId, user);
        if (root is null)
        {
            return Array.Empty<BaseItem>();
        }

        if (root is Movie or Episode)
        {
            return new[] { root };
        }

        return _library.GetItemList(new InternalItemsQuery(user)
        {
            AncestorIds = new[] { rootId },
            IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
            Recursive = true,
            IsVirtualItem = false,
            OrderBy = new[] { (ItemSortBy.ParentIndexNumber, SortOrder.Ascending), (ItemSortBy.IndexNumber, SortOrder.Ascending) },
        }).Where(v => v.IsVisibleStandalone(user)); // AncestorIds bypasses the library filter (box sets span libraries)
    }

    /// <summary>The item when the user may see it: parental rating AND access to its library.</summary>
    private BaseItem? Visible(Guid itemId, User user) => _library.GetItemById<BaseItem>(itemId, user);

    private IEnumerable<ResolvedFile> FilesOf(User user, BaseItem video, bool allVersions, bool includeSubtitles)
    {
        var sources = _sources.GetStaticMediaSources(video, false, user);
        var played = _userData.GetUserData(user, video)?.Played ?? false;
        var episode = video as Episode;
        var title = episode?.SeriesName ?? video.Name;

        foreach (var source in allVersions ? sources : sources.Take(1))
        {
            if (string.IsNullOrEmpty(source.Path) || !File.Exists(source.Path))
            {
                continue;
            }

            yield return new ResolvedFile(Guid.Parse(source.Id), source.Id, Path.GetFileName(source.Path),
                source.Size ?? new FileInfo(source.Path).Length, "video", null, title,
                episode?.ParentIndexNumber, episode?.IndexNumber, source.Name, played);

            if (!includeSubtitles)
            {
                continue;
            }

            foreach (var sub in source.MediaStreams.Where(m => m.Type == MediaStreamType.Subtitle && m.IsExternal && File.Exists(m.Path)))
            {
                yield return new ResolvedFile(Guid.Parse(source.Id), source.Id, Path.GetFileName(sub.Path),
                    new FileInfo(sub.Path).Length, "subtitle", sub.Index, title,
                    episode?.ParentIndexNumber, episode?.IndexNumber, source.Name, played);
            }
        }
    }
}
