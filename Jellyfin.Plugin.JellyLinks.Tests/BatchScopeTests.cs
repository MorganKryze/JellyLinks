using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Library;
using Xunit;

namespace JellyLinks.Tests;

public class BatchScopeTests
{
    private static ResolvedFile Ep(string show, int s, int e, string kind = "video") =>
        new(Guid.NewGuid(), "m", "f.mkv", 1_000, kind, null, show, s, e, null, false);

    private static ResolvedFile Movie(string title) =>
        new(Guid.NewGuid(), "m", "m.mkv", 1_000, "video", null, title, null, null, null, false);

    [Fact]
    public void One_season_with_contiguous_episodes()
    {
        var scope = BatchScope.From(new[] { Ep("Andor", 2, 1), Ep("Andor", 2, 2), Ep("Andor", 2, 2, "subtitle"), Ep("Andor", 2, 3) });
        Assert.Equal("Andor · Season 2 · E01–E03", scope.Title("en"));
        Assert.Equal("Andor · Saison 2 · E01–E03", scope.Title("fr"));
    }

    [Fact]
    public void Several_seasons()
    {
        var scope = BatchScope.From(new[] { Ep("Andor", 1, 1), Ep("Andor", 2, 1) });
        Assert.Equal("Andor · 2 seasons", scope.Title("en"));
        Assert.Equal("Andor · 2 saisons", scope.Title("fr"));
    }

    [Fact]
    public void Specials_single_and_gapped_episodes()
    {
        Assert.Equal("Andor · Spéciaux · E04", BatchScope.From(new[] { Ep("Andor", 0, 4) }).Title("fr"));
        Assert.Equal("Andor · Season 1 · 3 episodes", BatchScope.From(new[] { Ep("Andor", 1, 1), Ep("Andor", 1, 3), Ep("Andor", 1, 5) }).Title("en"));
    }

    [Fact]
    public void Movies_and_mixed_titles()
    {
        Assert.Equal("Dune: Part Two", BatchScope.From(new[] { Movie("Dune: Part Two") }).Title("en"));
        Assert.Equal("A, B", BatchScope.From(new[] { Movie("A"), Movie("B") }).Title("fr"));
        Assert.Equal("A, B +2", BatchScope.From(new[] { Movie("A"), Movie("B"), Movie("C"), Ep("D", 1, 1) }).Title("en"));
    }

    [Fact]
    public void Legacy_rows_keep_their_label_head()
    {
        Assert.Equal("Andor — Saison 2", BatchScope.Display("Andor — Saison 2 · 3 fichiers · 8,0 Go", null));
        var scope = BatchScope.From(new[] { Ep("Andor", 2, 1) });
        Assert.Equal("Andor · Season 2 · E01", BatchScope.Display("Andor · Season 2 · E01", scope));
    }
}
