using Jellyfin.Plugin.JellyLinks.Serving;
using Xunit;

namespace JellyLinks.Tests;

public class CountingCopyTests
{
    private sealed class BreaksAfter(long limit) : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (Length + buffer.Length > limit)
            {
                throw new IOException("client went away");
            }

            await base.WriteAsync(buffer, ct);
        }
    }

    [Fact]
    public async Task Copies_exactly_count_bytes_and_flushes_by_step()
    {
        var src = new MemoryStream(new byte[1_000_000]);
        var dst = new MemoryStream();
        var flushes = new List<long>();

        var written = await CountingCopy.CopyAsync(src, dst, 700_000, 262_144, flushes.Add, CancellationToken.None);

        Assert.Equal(700_000, written);
        Assert.Equal(700_000, dst.Length);
        Assert.Equal(700_000, flushes.Sum());
        Assert.True(flushes.Count >= 3);
    }

    [Fact]
    public async Task A_client_that_leaves_counts_only_what_left()
    {
        var src = new MemoryStream(new byte[1_000_000]);
        var dst = new BreaksAfter(200_000);
        var flushes = new List<long>();

        var written = await CountingCopy.CopyAsync(src, dst, 1_000_000, 64_000_000, flushes.Add, CancellationToken.None);

        Assert.Equal(dst.Length, written);
        Assert.True(written <= 200_000);
        Assert.Equal(written, flushes.Sum());
    }

    [Fact]
    public async Task A_failing_flush_is_not_counted_twice()
    {
        var src = new MemoryStream(new byte[1_000_000]);
        var dst = new MemoryStream();
        var flushes = new List<long>();
        var failed = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CountingCopy.CopyAsync(src, dst, 1_000_000, 100_000, n =>
        {
            flushes.Add(n);
            if (!failed)
            {
                failed = true;
                throw new InvalidOperationException("tracker failed");
            }
        }, CancellationToken.None));

        Assert.True(flushes.Sum() <= dst.Length, $"flushed {flushes.Sum()} for {dst.Length} written");
    }
}
