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

        // 4–5. Jellyfin permission, re-checked on every request; then the file, found again after a rename if needed
        var location = _library.Locate(batch.UserId, link);
        if (location.Outcome != LocateOutcome.Found)
        {
            return Deny(location.Outcome == LocateOutcome.Forbidden ? GateOutcome.Forbidden : GateOutcome.Gone, link, batch);
        }

        var found = location.File!;
        if (found.ItemId != link.ItemId || found.StreamIndex != link.StreamIndex
            || !string.Equals(found.MediaSourceId, link.MediaSourceId, StringComparison.OrdinalIgnoreCase))
        {
            long size;
            try
            {
                size = new FileInfo(found.Path).Length;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return Deny(GateOutcome.Gone, link, batch); // vanished since Locate
            }

            _store.Relink(link.Id, found.ItemId, found.MediaSourceId, found.StreamIndex, size);
            link = _store.GetLink(link.Id)!;
            await Publish(EventKind.LinkMoved, batch, $"{link.FileName} retrouvé après renommage").ConfigureAwait(false);
        }

        var path = found.Path;

        if (isHead)
        {
            return new GateResult(GateOutcome.Serve, link, batch, path, false);
        }

        // 6. addresses: recorded atomically, announced once per (batch, address)
        var limit = batch.IpLimitOverride ?? _config().IpLimit;
        var ipCheck = _store.AdmitIp(batch.Id, ip, limit, now);
        if (ipCheck.OverLimit)
        {
            var reason = $"{ipCheck.Distinct} adresses distinctes (limite {limit})";
            if (_store.BlockIfActive(batch.Id, reason)) // stored: no address, it would outlive retention
            {
                await Publish(EventKind.BatchBlocked, batch, $"{reason}, dernière : {ip}").ConfigureAwait(false);
            }

            return Deny(GateOutcome.Forbidden, link, batch);
        }

        if (ipCheck.IsNew)
        {
            await Publish(EventKind.NewIp, batch, $"adresse {ip} ({ipCheck.Distinct}/{(limit > 0 ? limit : "∞")})").ConfigureAwait(false);
        }

        // 7. quota
        var q = _quotas.GetStatus(batch.UserId);
        if (q.VolumeExhausted)
        {
            await Publish(EventKind.QuotaReached, batch, $"{q.UsedBytes} octets sur {q.Quota.VolumeBytes} ({q.Quota.PeriodDays} j)").ConfigureAwait(false);
            return Deny(GateOutcome.TooManyRequests, link, batch);
        }

        return new GateResult(GateOutcome.Serve, link, batch, path, ipCheck.IsNew);
    }

    private static GateResult Deny(GateOutcome o, LinkRecord? l = null, BatchRecord? b = null) => new(o, l, b, null, false);

    private Task Publish(EventKind kind, BatchRecord batch, string detail) =>
        _notifier.PublishAsync(new LinkEvent(kind, batch.UserId, _library.UserName(batch.UserId), batch.Label, batch.Id, detail));
}
