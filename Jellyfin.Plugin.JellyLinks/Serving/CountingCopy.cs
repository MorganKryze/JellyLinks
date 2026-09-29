namespace Jellyfin.Plugin.JellyLinks.Serving;

/// <summary>Copies a span and reports bytes that actually reached the client.</summary>
public static class CountingCopy
{
    private const int BufferSize = 81_920;

    public static async Task<long> CopyAsync(
        Stream source, Stream destination, long count, long flushEvery, Action<long> onFlush, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long written = 0;
        long pending = 0;
        try
        {
            while (written < count)
            {
                var want = (int)Math.Min(buffer.Length, count - written);
                var read = await source.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;
                pending += read;
                if (pending >= flushEvery)
                {
                    var flush = pending;
                    pending = 0; // before the callback: if it throws, finally must not report these bytes again
                    onFlush(flush);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // The client left: what was written so far is the truth.
        }
        finally
        {
            if (pending > 0)
            {
                onFlush(pending);
            }
        }

        return written;
    }
}
