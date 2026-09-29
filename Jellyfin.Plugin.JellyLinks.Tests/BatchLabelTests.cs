using Jellyfin.Plugin.JellyLinks.Api;
using Jellyfin.Plugin.JellyLinks.Library;
using Xunit;

namespace JellyLinks.Tests;

public class BatchLabelTests
{
    private static ResolvedFile Ep(string show, int s, int e, long size, string kind = "video") =>
        new(Guid.NewGuid(), "m", "f.mkv", size, kind, null, show, s, e, null, false);

    [Fact]
    public void One_season_of_one_show()
    {
        var files = new[] { Ep("Andor", 2, 1, 4_000_000_000), Ep("Andor", 2, 2, 4_000_000_000), Ep("Andor", 2, 2, 100, "subtitle") };
        Assert.Equal("Andor — Saison 2 · 3 fichiers · 8,0 Go", BatchLabel.For(files));
    }

    [Fact]
    public void Several_seasons()
    {
        var files = new[] { Ep("Andor", 1, 1, 1_000_000_000), Ep("Andor", 2, 1, 1_000_000_000) };
        Assert.Equal("Andor — 2 saisons · 2 fichiers · 2,0 Go", BatchLabel.For(files));
    }

    [Fact]
    public void Single_movie_uses_its_title()
    {
        var files = new[] { new ResolvedFile(Guid.NewGuid(), "m", "d.mkv", 71_300_000_000, "video", null, "Dune : Deuxième partie", null, null, null, false) };
        Assert.Equal("Dune : Deuxième partie · 1 fichier · 71,3 Go", BatchLabel.For(files));
    }

    [Fact]
    public void Mixed_titles()
    {
        var files = new[]
        {
            new ResolvedFile(Guid.NewGuid(), "m", "a.mkv", 1_000_000_000, "video", null, "A", null, null, null, false),
            new ResolvedFile(Guid.NewGuid(), "m", "b.mkv", 1_000_000_000, "video", null, "B", null, null, null, false),
        };
        Assert.Equal("A, B · 2 fichiers · 2,0 Go", BatchLabel.For(files));
    }
}
