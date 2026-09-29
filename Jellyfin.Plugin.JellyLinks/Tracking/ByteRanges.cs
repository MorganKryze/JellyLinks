using System.Globalization;

namespace Jellyfin.Plugin.JellyLinks.Tracking;

/// <summary>Sorted, merged, inclusive byte ranges served for one session.</summary>
public static class ByteRanges
{
    public static IReadOnlyList<(long Start, long End)> Add(IReadOnlyList<(long Start, long End)> ranges, long start, long end)
    {
        var all = new List<(long Start, long End)>(ranges) { (start, end) };
        all.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(long Start, long End)>();
        foreach (var r in all)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End + 1)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    public static bool Covers(IReadOnlyList<(long Start, long End)> ranges, long size) =>
        size > 0 && ranges.Count == 1 && ranges[0].Start == 0 && ranges[0].End >= size - 1;

    public static string Serialize(IReadOnlyList<(long Start, long End)> ranges) =>
        string.Join(',', ranges.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Start}-{r.End}")));

    public static long CoveredBytes(string ranges) =>
        Parse(ranges).Sum(r => r.End - r.Start + 1);

    public static IReadOnlyList<(long Start, long End)> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<(long, long)>();
        }

        return text.Split(',')
            .Select(p => p.Split('-'))
            .Select(p => (long.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();
    }
}
