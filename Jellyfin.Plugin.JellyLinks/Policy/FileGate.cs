using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Library;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Signing;

namespace Jellyfin.Plugin.JellyLinks.Policy;

public enum GateOutcome
{
    Serve,
    NotFound,
    Gone,
    Forbidden,
    TooManyRequests,
}

public sealed record GateResult(GateOutcome Outcome, LinkRecord? Link, BatchRecord? Batch, string? Path, bool IsNewIp);

/// <summary>The seven checks of a file request, in the order the spec fixes.</summary>
public sealed class FileGate
{
    private readonly LinkStore _store;
    private readonly LinkSigner _signer;
    private readonly ILibraryGateway _library;
    private readonly QuotaService _quotas;
    private readonly Notifier _notifier;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _clock;

    public FileGate(LinkStore store, LinkSigner signer, ILibraryGateway library, QuotaService quotas,
                    Notifier notifier, Func<PluginConfiguration> config, TimeProvider clock)
    {
        _store = store;
        _signer = signer;
        _library = library;
        _quotas = quotas;
        _notifier = notifier;
        _config = config;
        _clock = clock;
    }

    public async Task<GateResult> CheckAsync(string token, string ip, bool isHead)
    {
        // 1. signature
        if (!_signer.TryVerify(token, out var linkId, out var expires)
            || _store.GetLink(linkId) is not { } link
            || _store.GetBatch(link.BatchId) is not { } batch)
        {
            return Deny(GateOutcome.NotFound);
        }

        // 2. expiry
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (expires <= now || batch.ExpiresAt <= now || batch.State == BatchStates.Expired)
        {
            return Deny(GateOutcome.Gone, link, batch);
        }

        // 3. batch state
        if (batch.State != BatchStates.Active)
        {
            return Deny(GateOutcome.Forbidden, link, batch);
        }

        // 4. Jellyfin permission, re-checked on every request
        if (!_library.CanDownload(batch.UserId, link.ItemId))
        {
            return Deny(GateOutcome.Forbidden, link, batch);
        }

        // 5. file still there (path resolved now, never stored)
        var path = _library.ResolvePath(batch.UserId, link.ItemId, link.MediaSourceId, link.StreamIndex);
        if (path is null || !File.Exists(path))
        {
            return Deny(GateOutcome.Gone, link, batch);
        }

        if (isHead)
        {
            return new GateResult(GateOutcome.Serve, link, batch, path, false);
        }

        // 6. addresses
        var isNew = !_store.BatchHasIp(batch.Id, ip);
        string? newIpDetail = null;
        if (isNew)
        {
            var limit = batch.IpLimitOverride ?? _config().IpLimit;
            var distinct = _store.CountDistinctIps(batch.Id) + 1;
            if (limit > 0 && distinct > limit)
            {
                var detail = $"{distinct} adresses distinctes (limite {limit}), dernière : {ip}";
                _store.SetBatchState(batch.Id, BatchStates.Blocked, detail);
                await Publish(EventKind.BatchBlocked, batch, detail).ConfigureAwait(false);
                return Deny(GateOutcome.Forbidden, link, batch);
            }

            newIpDetail = $"adresse {ip} ({distinct}/{(limit > 0 ? limit : "∞")})";
        }

        // 7. quota
        var q = _quotas.GetStatus(batch.UserId);
        if (q.VolumeExhausted)
        {
            await Publish(EventKind.QuotaReached, batch, $"{q.UsedBytes} octets sur {q.Quota.VolumeBytes} ({q.Quota.PeriodDays} j)").ConfigureAwait(false);
            return Deny(GateOutcome.TooManyRequests, link, batch);
        }

        if (newIpDetail is not null)
        {
            await Publish(EventKind.NewIp, batch, newIpDetail).ConfigureAwait(false);
        }

        return new GateResult(GateOutcome.Serve, link, batch, path, isNew);
    }

    private static GateResult Deny(GateOutcome o, LinkRecord? l = null, BatchRecord? b = null) => new(o, l, b, null, false);

    private Task Publish(EventKind kind, BatchRecord batch, string detail) =>
        _notifier.PublishAsync(new LinkEvent(kind, batch.UserId, _library.UserName(batch.UserId), batch.Label, batch.Id, detail));
}
