using System.Globalization;

namespace Jellyfin.Plugin.JellyLinks.Serving;

public enum RangeKind
{
    Full,
    Partial,
    Unsatisfiable,
}

/// <summary>Inclusive byte span to serve.</summary>
public readonly record struct RangeRequest(RangeKind Kind, long Start, long End);

/// <summary>Single-range parser. Anything unreadable or multi-range is served whole (RFC 9110 §14.2).</summary>
public static class RangeParser
{
    /// <summary>Strong validator of a served file: changes when the file is replaced (size or modification time).</summary>
    public static string ETag(long size, DateTime lastWriteUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"\"{size:x}-{lastWriteUtc.Ticks:x}\"");

    /// <summary>
    /// RFC 9110 §13.1.5: a Range is honoured only while If-Range (when sent) still names this exact file; otherwise
    /// the whole file must be sent, so a resumed download never mixes two versions.
    /// </summary>
    public static bool IfRangeHolds(string? ifRange, string etag, DateTime lastWriteUtc)
    {
        if (string.IsNullOrWhiteSpace(ifRange))
        {
            return true;
        }

        var value = ifRange.Trim();
        if (value.StartsWith('"'))
        {
            return string.Equals(value, etag, StringComparison.Ordinal);
        }

        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            return false; // a weak validator never satisfies If-Range
        }

        var seconds = new DateTime(lastWriteUtc.Ticks - (lastWriteUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            && date.UtcDateTime == seconds;
    }

    public static RangeRequest Parse(string? header, long size)
    {
        var full = new RangeRequest(RangeKind.Full, 0, size - 1);
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.Ordinal))
        {
            return full;
        }

        var spec = header[6..].Trim();
        if (spec.Contains(','))
        {
            return full;
        }

        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return full;
        }

        var left = spec[..dash];
        var right = spec[(dash + 1)..];

        if (left.Length == 0)
        {
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix == 0)
            {
                return full;
            }

            return new RangeRequest(RangeKind.Partial, Math.Max(0, size - suffix), size - 1);
        }

        if (!long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return full;
        }

        long end = size - 1;
        if (right.Length > 0)
        {
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
            {
                return full;
            }
        }

        if (start >= size)
        {
            return new RangeRequest(RangeKind.Unsatisfiable, 0, 0);
        }

        return new RangeRequest(RangeKind.Partial, start, Math.Min(end, size - 1));
    }
}
