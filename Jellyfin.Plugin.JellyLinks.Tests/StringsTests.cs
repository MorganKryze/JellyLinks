using Jellyfin.Plugin.JellyLinks.I18n;
using Xunit;

namespace JellyLinks.Tests;

public class StringsTests
{
    private static Dictionary<string, string> N(long n) => new() { ["n"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr", "fr")]
    [InlineData("FR-ca", "fr")]
    [InlineData("en-US", "en")]
    [InlineData(null, "en")]
    public void Culture_maps_to_a_shipped_language(string? culture, string lang) => Assert.Equal(lang, Strings.Lang(culture));

    [Fact]
    public void Unknown_culture_is_english() => Assert.Equal("Download links", Strings.T(Strings.Lang("de-DE"), "gen.title"));

    [Fact]
    public void Placeholders_are_replaced() =>
        Assert.Equal("Saison 2", Strings.T("fr", "season", N(2)));

    [Fact]
    public void French_counts_zero_and_one_as_singular()
    {
        Assert.Equal("0 fichier", Strings.T("fr", "ev.created", N(0)));
        Assert.Equal("1 fichier", Strings.T("fr", "ev.created", N(1)));
        Assert.Equal("2 fichiers", Strings.T("fr", "ev.created", N(2)));
        Assert.Equal("0 files", Strings.T("en", "ev.created", N(0)));
        Assert.Equal("1 file", Strings.T("en", "ev.created", N(1)));
    }

    [Fact]
    public void Missing_key_is_the_key_itself() => Assert.Equal("nope.nothing", Strings.T("fr", "nope.nothing"));

    [Theory]
    [InlineData("en", 999, "999 B")]
    [InlineData("en", 1500, "1.5 KB")]
    [InlineData("fr", 1500, "1,5 Ko")]
    [InlineData("fr", 96_400_000_000, "96,4 Go")]
    public void Sizes_follow_the_language(string lang, long n, string expected) => Assert.Equal(expected, Strings.Bytes(lang, n));

    [Fact]
    public void Server_language_reads_the_culture_each_time()
    {
        var culture = "en-US";
        var lang = new ServerLanguage(() => culture);
        Assert.Equal("en", lang.Current);
        culture = "fr-FR";
        Assert.Equal("fr", lang.Current);
    }
}
