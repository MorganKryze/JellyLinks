using Jellyfin.Plugin.JellyLinks.Tracking;
using Xunit;

namespace JellyLinks.Tests;

public class ByteRangesTests
{
    private static IReadOnlyList<(long, long)> Build(params (long, long)[] parts)
    {
        IReadOnlyList<(long, long)> r = Array.Empty<(long, long)>();
        foreach (var (s, e) in parts)
        {
            r = ByteRanges.Add(r, s, e);
        }

        return r;
    }

    [Fact]
    public void Overlapping_and_adjacent_ranges_merge()
    {
        var r = Build((0, 99), (50, 149), (150, 199));
        Assert.Equal(new[] { (0L, 199L) }, r);
    }

    [Fact]
    public void Gaps_are_kept_and_sorted()
    {
        var r = Build((300, 399), (0, 99));
        Assert.Equal(new[] { (0L, 99L), (300L, 399L) }, r);
    }

    [Fact]
    public void Covers_needs_every_byte_including_the_last()
    {
        Assert.True(ByteRanges.Covers(Build((0, 999)), 1000));
        Assert.False(ByteRanges.Covers(Build((0, 998)), 1000));
        Assert.False(ByteRanges.Covers(Build((1, 999)), 1000));
        Assert.False(ByteRanges.Covers(Build((0, 499), (501, 999)), 1000));
    }

    [Fact]
    public void Parallel_chunks_out_of_order_cover_the_file()
    {
        var r = Build((750, 999), (0, 249), (500, 749), (250, 499));
        Assert.True(ByteRanges.Covers(r, 1000));
    }

    [Fact]
    public void Serialize_round_trips()
    {
        var r = Build((0, 99), (200, 399));
        Assert.Equal("0-99,200-399", ByteRanges.Serialize(r));
        Assert.Equal(r, ByteRanges.Parse("0-99,200-399"));
        Assert.Empty(ByteRanges.Parse(string.Empty));
    }
}
