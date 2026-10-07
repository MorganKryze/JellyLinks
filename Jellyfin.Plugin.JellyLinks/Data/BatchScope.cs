using System.Globalization;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Library;

namespace Jellyfin.Plugin.JellyLinks.Data;

public sealed record ScopeSeason(int Number, IReadOnlyList<int> Episodes);

public sealed record ScopeTitle(string Name, IReadOnlyList<ScopeSeason> Seasons);

/// <summary>What a batch holds, stored as data so each reader gets its title in its own language.</summary>
public sealed record BatchScope(IReadOnlyList<ScopeTitle> Titles)
{
    public static BatchScope From(IReadOnlyList<ResolvedFile> files) =>
        new(files.GroupBy(f => f.Title).Select(t => new ScopeTitle(
            t.Key,
            t.Where(f => f.SeasonNumber is not null).GroupBy(f => f.SeasonNumber!.Value).OrderBy(s => s.Key)
                .Select(s => new ScopeSeason(s.Key, s.Where(f => f.EpisodeNumber is not null).Select(f => f.EpisodeNumber!.Value).Distinct().Order().ToList()))
                .ToList())).ToList());

    /// <summary>The title to show: a 0.2.x label ("Andor — Saison 2 · 3 fichiers · 8,0 Go") loses its size tail.</summary>
    public static string Display(string label, BatchScope? scope) => scope is null ? label.Split(" · ")[0] : label;

    /// <summary>The title of a batch in today's language: rendered from its scope, else the trimmed 0.2.x label.</summary>
    public static string Now(BatchRecord batch, string lang) => batch.Scope is { } s ? s.Title(lang) : Display(batch.Label, null);

    public string Title(string lang)
    {
        if (Titles.Count == 0)
        {
            return string.Empty;
        }

        if (Titles.Count > 1)
        {
            var head = string.Join(", ", Titles.Take(2).Select(t => t.Name));
            return Titles.Count > 2 ? $"{head} +{Titles.Count - 2}" : head;
        }

        var only = Titles[0];
        if (only.Seasons.Count == 0)
        {
            return only.Name;
        }

        if (only.Seasons.Count > 1)
        {
            return $"{only.Name} · {Strings.T(lang, "head.seasons", N(only.Seasons.Count))}";
        }

        var s = only.Seasons[0];
        var season = s.Number == 0 ? Strings.T(lang, "specials") : Strings.T(lang, "season", N(s.Number));
        var episodes = Episodes(lang, s.Episodes);
        return episodes.Length == 0 ? $"{only.Name} · {season}" : $"{only.Name} · {season} · {episodes}";
    }

    private static string Episodes(string lang, IReadOnlyList<int> e)
    {
        if (e.Count == 0)
        {
            return string.Empty;
        }

        if (e.Count == 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"E{e[0]:00}");
        }

        return e[^1] - e[0] == e.Count - 1
            ? string.Create(CultureInfo.InvariantCulture, $"E{e[0]:00}–E{e[^1]:00}")
            : Strings.T(lang, "head.episodes", N(e.Count));
    }

    private static Dictionary<string, string> N(int n) => new() { ["n"] = n.ToString(CultureInfo.InvariantCulture) };
}
