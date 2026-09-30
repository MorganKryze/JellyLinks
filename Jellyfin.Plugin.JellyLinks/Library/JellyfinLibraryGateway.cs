using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyLinks.Data;
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

        // An alternate version can also come back as its own item: keep each file once.
        return files.DistinctBy(f => (f.MediaSourceId, f.StreamIndex)).ToList();
    }

    public Location Locate(Guid userId, LinkRecord link)
    {
        var user = _users.GetUserById(userId);
        if (user is null || !user.HasPermission(PermissionKind.EnableContentDownloading))
        {
            return Location.Forbidden;
        }

        var key = FallbackKey.FromJson(link.Fallback);
        var item = Visible(link.ItemId, user);
        if (item is not null)
        {
            var here = FileIn(user, item, link, key, byKey: false);
            if (here is not null)
            {
                return Location.Found(here);
            }
        }
        else if (_library.GetItemById(link.ItemId) is not null)
        {
            return Location.Forbidden; // still in the library, not for this user
        }

        // Renamed (new id), or a ghost row whose file is gone: look for exactly one replacement.
        if (key is null)
        {
            return Location.Gone;
        }

        var candidates = Candidates(user, key);
        if (candidates.Count != 1)
        {
            return Location.Gone; // none, or ambiguous: never guess
        }

        var moved = FileIn(user, candidates[0], link, key, byKey: true);
        return moved is null ? Location.Gone : Location.Found(moved);
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
                episode?.ParentIndexNumber, episode?.IndexNumber, source.Name, played, episode?.Name,
                KeyOf(video, sources.Count, source.Name, null).ToJson());

            if (!includeSubtitles)
            {
                continue;
            }

            foreach (var sub in source.MediaStreams.Where(m => m.Type == MediaStreamType.Subtitle && m.IsExternal && File.Exists(m.Path)))
            {
                yield return new ResolvedFile(Guid.Parse(source.Id), source.Id, Path.GetFileName(sub.Path),
                    new FileInfo(sub.Path).Length, "subtitle", sub.Index, title,
                    episode?.ParentIndexNumber, episode?.IndexNumber, source.Name, played, episode?.Name,
                    KeyOf(video, sources.Count, source.Name, FallbackKey.SuffixOf(Path.GetFileName(source.Path), Path.GetFileName(sub.Path))).ToJson());
            }
        }
    }

    private static FallbackKey KeyOf(BaseItem video, int versionCount, string? version, string? subtitleSuffix)
    {
        var episode = video as Episode;
        return new FallbackKey(
            episode is null ? "Movie" : "Episode",
            new Dictionary<string, string>(video.ProviderIds ?? new Dictionary<string, string>()),
            video.Name,
            video.ProductionYear,
            episode?.SeriesPresentationUniqueKey,
            episode?.Series?.ProviderIds is { } sp ? new Dictionary<string, string>(sp) : null,
            episode?.ParentIndexNumber,
            episode?.IndexNumber,
            version,
            versionCount,
            subtitleSuffix,
            video.GetTopParent()?.Id);
    }

    /// <summary>The link's file on this item: the stored source (or, after a rename, the one the key picks); subtitles by suffix first.</summary>
    private LocatedFile? FileIn(User user, BaseItem item, LinkRecord link, FallbackKey? key, bool byKey)
    {
        var sources = _sources.GetStaticMediaSources(item, false, user).ToList();
        MediaSourceInfo? source;
        if (byKey)
        {
            var pick = key?.PickSource(sources.Select(s => s.Name).ToList());
            source = pick is int i ? sources[i] : null;
        }
        else
        {
            source = sources.FirstOrDefault(s => string.Equals(s.Id, link.MediaSourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (source is null || string.IsNullOrEmpty(source.Path))
        {
            return null;
        }

        if (link.Kind != "subtitle")
        {
            return File.Exists(source.Path) ? new LocatedFile(source.Path, Guid.Parse(source.Id), source.Id, null) : null;
        }

        var subs = source.MediaStreams
            .Where(m => m.Type == MediaStreamType.Subtitle && m.IsExternal && !string.IsNullOrEmpty(m.Path))
            .ToList();
        var bySuffix = FallbackKey.PickSubtitle(Path.GetFileName(source.Path), subs.Select(m => m.Path).ToList(), key?.SubtitleSuffix);
        var sub = bySuffix is int j ? subs[j] : (byKey ? null : subs.FirstOrDefault(m => m.Index == link.StreamIndex));
        return sub is not null && File.Exists(sub.Path) ? new LocatedFile(sub.Path, Guid.Parse(source.Id), source.Id, sub.Index) : null;
    }

    private List<BaseItem> Candidates(User user, FallbackKey key)
    {
        IEnumerable<BaseItem> found = Array.Empty<BaseItem>();
        if (key.Kind == "Episode" && key.Season is int season && key.Episode is int number)
        {
            found = Episodes(user, key.SeriesKey, season, number);
            if (!found.Any() && key.SeriesProviderIds is { Count: > 0 } sp)
            {
                var series = Query(user, BaseItemKind.Series, q => q.HasAnyProviderId = new Dictionary<string, string>(sp));
                found = series.Count == 1 ? Episodes(user, series[0].PresentationUniqueKey, season, number) : Array.Empty<BaseItem>();
            }
        }
        else if (key.ProviderIds.Count > 0)
        {
            var kind = key.Kind == "Episode" ? BaseItemKind.Episode : BaseItemKind.Movie;
            found = Query(user, kind, q => q.HasAnyProviderId = new Dictionary<string, string>(key.ProviderIds))
                .Where(c => key.ProviderIds.All(p => c.ProviderIds is null || !c.ProviderIds.TryGetValue(p.Key, out var v)
                    || string.Equals(v, p.Value, StringComparison.OrdinalIgnoreCase)));
        }
        else if (key.Kind == "Movie" && key.Year is int year)
        {
            found = Query(user, BaseItemKind.Movie, q =>
            {
                q.Name = key.Name;
                q.Years = new[] { year };
            });
        }

        // Series / ancestor filters skip Jellyfin's per-user library filter: re-check every candidate.
        // The same film can sit in two libraries (HD and 4K): only the original library counts.
        return found
            .Where(c => key.LibraryId is not Guid library || c.GetTopParent()?.Id == library)
            .Where(c => Visible(c.Id, user) is not null)
            .ToList();
    }

    private IEnumerable<BaseItem> Episodes(User user, string? seriesKey, int season, int number) =>
        string.IsNullOrEmpty(seriesKey)
            ? Array.Empty<BaseItem>()
            : Query(user, BaseItemKind.Episode, q =>
            {
                q.SeriesPresentationUniqueKey = seriesKey;
                q.ParentIndexNumber = season;
                q.IndexNumber = number;
            });

    private List<BaseItem> Query(User user, BaseItemKind kind, Action<InternalItemsQuery> filter)
    {
        var q = new InternalItemsQuery(user) { IncludeItemTypes = new[] { kind }, Recursive = true, IsVirtualItem = false };
        filter(q);
        return _library.GetItemList(q).ToList();
    }
}
