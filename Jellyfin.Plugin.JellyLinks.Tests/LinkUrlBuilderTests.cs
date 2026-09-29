using Jellyfin.Plugin.JellyLinks.Serving;
using Xunit;

namespace JellyLinks.Tests;

public class LinkUrlBuilderTests
{
    [Theory]
    [InlineData("Star Wars (1977) {tmdb-11} - 4KLight.mkv", "Star%20Wars%20%281977%29%20%7Btmdb-11%7D%20-%204KLight.mkv")]
    [InlineData("8+3= #1 ?.mkv", "8%2B3%3D%20%231%20%3F.mkv")]
    [InlineData("Amélie 100%.mkv", "Am%C3%A9lie%20100%25.mkv")]
    public void Names_are_escaped_into_one_path_segment(string name, string escaped)
    {
        Assert.Equal($"https://jellyfin.example/JellyLinks/f/TOKEN/{escaped}",
            LinkUrlBuilder.Build("https://jellyfin.example/", "TOKEN", name));
    }

    [Fact]
    public void Base_path_is_kept()
    {
        Assert.Equal("https://h.example/jf/JellyLinks/f/T/a.mkv", LinkUrlBuilder.Build("https://h.example/jf", "T", "a.mkv"));
    }
}
