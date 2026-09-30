using System.Text.Json;

namespace Jellyfin.Plugin.JellyLinks.Library;

/// <summary>
/// What identifies a link's file besides its Jellyfin id, which is derived from the path and changes on rename.
/// Kind is "Movie" or "Episode"; for a movie the key describes the primary item (alternates are reached through it).
/// LibraryId is the item's top parent: a replacement is only looked for in the same library.
/// </summary>
public sealed record FallbackKey(
    string Kind,
    IReadOnlyDictionary<string, string> ProviderIds,
    string Name,
    int? Year,
    string? SeriesKey,
    IReadOnlyDictionary<string, string>? SeriesProviderIds,
    int? Season,
    int? Episode,
    string? Version,
    int VersionCount,
    string? SubtitleSuffix,
    Guid? LibraryId = null)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static FallbackKey? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FallbackKey>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Index of the source to serve: the one with the same version name, else the only source when the file had a single
    /// version at creation. Null when it would be a guess.
    /// </summary>
    public int? PickSource(IReadOnlyList<string?> sourceNames)
    {
        var same = Enumerable.Range(0, sourceNames.Count)
            .Where(i => Version is not null && string.Equals(sourceNames[i], Version, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (same.Count == 1)
        {
            return same[0];
        }

        return VersionCount == 1 && sourceNames.Count == 1 ? 0 : null;
    }

    /// <summary>".fr.srt" for "Film (2020).fr.srt" next to "Film (2020).mkv"; null when the names do not share a stem.</summary>
    public static string? SuffixOf(string videoFileName, string subtitleFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(videoFileName);
        return subtitleFileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ? subtitleFileName[stem.Length..] : null;
    }

    /// <summary>Index of the single subtitle whose suffix matches; null when none or several do.</summary>
    public static int? PickSubtitle(string videoFileName, IReadOnlyList<string> subtitlePaths, string? suffix)
    {
        if (suffix is null)
        {
            return null;
        }

        var hits = Enumerable.Range(0, subtitlePaths.Count)
            .Where(i => string.Equals(SuffixOf(videoFileName, Path.GetFileName(subtitlePaths[i])), suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return hits.Count == 1 ? hits[0] : null;
    }
}
