using Jellyfin.Plugin.JellyLinks.Serving;
using Xunit;

namespace JellyLinks.Tests;

public class RangeParserTests
{
    [Fact]
    public void No_header_is_full()
    {
        Assert.Equal(new RangeRequest(RangeKind.Full, 0, 999), RangeParser.Parse(null, 1000));
    }

    [Theory]
    [InlineData("bytes=0-99", 0, 99)]
    [InlineData("bytes=900-", 900, 999)]
    [InlineData("bytes=-100", 900, 999)]
    [InlineData("bytes=950-5000", 950, 999)]
    public void Single_ranges_are_clamped_to_the_file(string header, long s, long e)
    {
        Assert.Equal(new RangeRequest(RangeKind.Partial, s, e), RangeParser.Parse(header, 1000));
    }

    [Fact]
    public void Start_beyond_the_end_is_unsatisfiable()
    {
        Assert.Equal(RangeKind.Unsatisfiable, RangeParser.Parse("bytes=1000-", 1000).Kind);
    }

    [Theory]
    [InlineData("bytes=0-99,200-299")]
    [InlineData("items=0-10")]
    [InlineData("bytes=abc")]
    [InlineData("bytes=50-10")]
    public void Unreadable_or_multi_range_is_served_whole(string header)
    {
        Assert.Equal(new RangeRequest(RangeKind.Full, 0, 999), RangeParser.Parse(header, 1000));
    }

    private static readonly DateTime Written = new(2026, 9, 30, 12, 0, 0, 500, DateTimeKind.Utc);

    [Fact]
    public void Etag_changes_when_the_file_is_replaced()
    {
        var e = RangeParser.ETag(1000, Written);
        Assert.StartsWith("\"", e);
        Assert.NotEqual(e, RangeParser.ETag(1001, Written));
        Assert.NotEqual(e, RangeParser.ETag(1000, Written.AddSeconds(1)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("SAME", true)]
    [InlineData("\"other\"", false)]
    [InlineData("W/SAME", false)]
    [InlineData("Wed, 30 Sep 2026 12:00:00 GMT", true)]
    [InlineData("Wed, 30 Sep 2026 12:00:01 GMT", false)]
    [InlineData("not a date", false)]
    public void If_range_honours_the_range_only_for_the_same_file(string? header, bool holds)
    {
        var etag = RangeParser.ETag(1000, Written);
        var value = header?.Replace("SAME", etag);
        Assert.Equal(holds, RangeParser.IfRangeHolds(value, etag, Written));
    }
}
