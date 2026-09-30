using Jellyfin.Plugin.JellyLinks.Library;
using Xunit;

namespace JellyLinks.Tests;

public class FallbackKeyTests
{
    private static FallbackKey Movie(string? version, int count) =>
        new("Movie", new Dictionary<string, string>(), "Dune", 2024, null, null, null, null, version, count, null);

    [Fact]
    public void Round_trips_through_json()
    {
        var k = new FallbackKey("Episode", new Dictionary<string, string> { ["Tvdb"] = "1" }, "E1", null, "key",
            new Dictionary<string, string> { ["Tmdb"] = "8666" }, 1, 2, null, 1, ".fr.srt");
        var back = FallbackKey.FromJson(k.ToJson())!;
        Assert.Equal("key", back.SeriesKey);
        Assert.Equal(2, back.Episode);
        Assert.Equal("8666", back.SeriesProviderIds!["Tmdb"]);
        Assert.Null(FallbackKey.FromJson(null));
        Assert.Null(FallbackKey.FromJson("{not json"));
    }

    [Fact]
    public void Picks_the_only_source_the_same_version_or_the_default_one()
    {
        Assert.Equal(0, Movie("1080p", 2).PickSource(new string?[] { "2160p" }));
        Assert.Equal(1, Movie("720p", 2).PickSource(new string?[] { "1080p", "720p" }));
        Assert.Equal(0, Movie("Dune", 1).PickSource(new string?[] { "2160p", "1080p" }));
    }

    [Fact]
    public void Two_versions_without_the_same_name_are_ambiguous()
    {
        Assert.Null(Movie("720p", 2).PickSource(new string?[] { "2160p", "1080p" }));
        Assert.Null(Movie("720p", 2).PickSource(Array.Empty<string?>()));
    }

    [Fact]
    public void Subtitles_are_found_by_their_suffix_after_a_rename()
    {
        Assert.Equal(".fr.srt", FallbackKey.SuffixOf("Film (2020).mkv", "Film (2020).fr.srt"));
        Assert.Null(FallbackKey.SuffixOf("Film (2020).mkv", "Other.fr.srt"));
        var subs = new[] { "/m/Film (2020) - 1080p.en.srt", "/m/Film (2020) - 1080p.fr.srt" };
        Assert.Equal(1, FallbackKey.PickSubtitle("Film (2020) - 1080p.mkv", subs, ".fr.srt"));
        Assert.Null(FallbackKey.PickSubtitle("Film (2020) - 1080p.mkv", subs, ".de.srt"));
        Assert.Null(FallbackKey.PickSubtitle("Film (2020) - 1080p.mkv", subs, null));
    }
}
