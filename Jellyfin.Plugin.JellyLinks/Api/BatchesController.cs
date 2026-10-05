using System.Security.Claims;
using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Library;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Policy;
using Jellyfin.Plugin.JellyLinks.Serving;
using Jellyfin.Plugin.JellyLinks.Signing;
using Jellyfin.Plugin.JellyLinks.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyLinks.Api;

public sealed record CreateBatchRequest(
    IReadOnlyList<Guid> RootItemIds, bool AllVersions, bool IncludeSubtitles,
    IReadOnlyList<Guid>? ExcludedItemIds, IReadOnlyList<string>? ExcludedMediaSourceIds);

public sealed record FileView(Guid ItemId, string MediaSourceId, string FileName, long Size, string Kind,
    int? SeasonNumber, int? EpisodeNumber, string Title, string? VersionName, bool Played, string? ItemName);

public sealed record QuotaView(bool Enabled, long UsedBytes, long VolumeBytes, int PeriodDays, int ActiveBatches, int MaxActiveBatches, long? FreesAt = null);

public sealed record PreviewResponse(IReadOnlyList<FileView> Files, long TotalBytes, long ExpiresAt, QuotaView Quota, bool HasOtherVersions, bool HasSubtitles);

public sealed record LinkView(string FileName, long Size, string Kind, string Url, string Status, long BytesReceived);

public sealed record BatchResponse(long Id, string Label, BatchScope? Scope, long CreatedAt, long ExpiresAt, string State,
    int FileCount, long TotalBytes, int CompleteCount, IReadOnlyList<LinkView> Links);

[ApiController]
[Route("JellyLinks/batches")]
[Authorize]
public sealed class BatchesController : ControllerBase
{
    private readonly LinkStore _store;
    private readonly ILibraryGateway _library;
    private readonly QuotaService _quotas;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _clock;
    private readonly Notifier _notifier;
    private readonly SigningKey _key;
    private readonly ServerLanguage _language;

    public BatchesController(LinkStore store, ILibraryGateway library, QuotaService quotas,
                             Func<PluginConfiguration> config, TimeProvider clock, Notifier notifier, SigningKey key,
                             ServerLanguage language)
    {
        _store = store;
        _library = library;
        _quotas = quotas;
        _config = config;
        _clock = clock;
        _notifier = notifier;
        _key = key;
        _language = language;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue("Jellyfin-UserId")!);

    private bool CanDownload => _library.Users().FirstOrDefault(u => u.Id == UserId) is not { CanDownload: false };

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    private LinkSigner Signer => new(_key.Current);

    [HttpPost("preview")]
    public ActionResult<PreviewResponse> Preview([FromBody] CreateBatchRequest req)
    {
        var files = _library.Expand(UserId, req.RootItemIds, req.AllVersions, req.IncludeSubtitles);
        var everything = _library.Expand(UserId, req.RootItemIds, true, true);
        var q = _quotas.GetStatus(UserId);
        return new PreviewResponse(
            files.Select(f => new FileView(f.ItemId, f.MediaSourceId, f.FileName, f.Size, f.Kind, f.SeasonNumber,
                f.EpisodeNumber, f.Title, f.VersionName, f.Played, f.ItemName)).ToList(),
            files.Sum(f => f.Size),
            Now + (_config().LinkValidityDays * 86_400L),
            ToView(q),
            req.AllVersions || everything.Count(f => f.Kind == "video") > files.Count(f => f.Kind == "video"),
            everything.Any(f => f.Kind == "subtitle"));
    }

    [HttpGet("quota")]
    public ActionResult<QuotaView> Quota() => ToView(_quotas.GetStatus(UserId));

    [HttpPost]
    public ActionResult<BatchResponse> Create([FromBody] CreateBatchRequest req)
    {
        var selection = new Selection(req.RootItemIds, req.AllVersions, req.IncludeSubtitles,
            req.ExcludedItemIds ?? Array.Empty<Guid>(), req.ExcludedMediaSourceIds ?? Array.Empty<string>());
        return CreateFrom(selection);
    }

    [HttpGet("mine")]
    public ActionResult<IReadOnlyList<BatchResponse>> Mine() =>
        _store.GetBatchesForUser(UserId).Select(ToResponse).ToList();

    [HttpPost("{id:long}/revoke")]
    public IActionResult Revoke(long id)
    {
        var b = _store.GetBatch(id);
        if (b is null || b.UserId != UserId)
        {
            return NotFound();
        }

        if (BatchStates.Effective(b.State, b.ExpiresAt, Now) == BatchStates.Active)
        {
            _store.SetBatchState(id, BatchStates.Revoked, Msg.Of("revoked_user").Serialize());
            _ = _notifier.PublishAsync(new LinkEvent(EventKind.BatchRevoked, UserId, _library.UserName(UserId), b.Label, b.Id, Msg.Of("revoked_user")));
        }

        return NoContent();
    }

    [HttpPost("{id:long}/regenerate")]
    public ActionResult<BatchResponse> Regenerate(long id)
    {
        var b = _store.GetBatch(id);
        if (b is null || b.UserId != UserId)
        {
            return NotFound();
        }

        if (BatchStates.Effective(b.State, b.ExpiresAt, Now) == BatchStates.Blocked)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Msg.Of("blocked"));
        }

        return CreateFrom(b.Selection);
    }

    private ActionResult<BatchResponse> CreateFrom(Selection selection)
    {
        if (!CanDownload)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Msg.Of("not_allowed"));
        }

        var q = _quotas.GetStatus(UserId);
        if (q.BatchesExhausted || q.VolumeExhausted)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ToView(q));
        }

        var files = _library.Expand(UserId, selection.RootItemIds, selection.AllVersions, selection.IncludeSubtitles)
            .Where(f => !selection.ExcludedItemIds.Contains(f.ItemId) && !selection.ExcludedMediaSourceIds.Contains(f.MediaSourceId))
            .ToList();
        if (files.Count == 0)
        {
            return BadRequest(Msg.Of("empty"));
        }

        var now = Now;
        var scope = BatchScope.From(files);
        var label = scope.Title(_language.Current);
        var id = _store.CreateBatch(UserId, now, now + (_config().LinkValidityDays * 86_400L), label, selection,
            files.Select(f => new LinkRecord(0, 0, f.ItemId, f.MediaSourceId, f.FileName, f.Size, f.Kind, f.StreamIndex, f.Title, f.Fallback)).ToList(),
            scope);
        _ = _notifier.PublishAsync(new LinkEvent(EventKind.BatchCreated, UserId, _library.UserName(UserId), label, id,
            Msg.Of("created", ("n", files.Count))));
        return ToResponse(_store.GetBatch(id)!);
    }

    private BatchResponse ToResponse(BatchRecord b)
    {
        var state = BatchStates.Effective(b.State, b.ExpiresAt, Now);
        var baseUrl = LinkUrlBuilder.BaseUrl(_config().PublicBaseUrl, $"{Request.Scheme}://{Request.Host}{Request.PathBase}");
        var links = _store.GetLinks(b.Id);
        var views = new List<LinkView>();
        var complete = 0;
        foreach (var l in links)
        {
            if (l.Complete)
            {
                complete++;
            }

            var status = l.Complete ? SessionStatuses.Complete : _store.GetLatestSessionForLink(l.Id)?.Status ?? "pending";

            var url = state == BatchStates.Active ? LinkUrlBuilder.Build(baseUrl, Signer.Sign(l.Id, b.ExpiresAt), l.FileName) : string.Empty;
            views.Add(new LinkView(l.FileName, l.Size, l.Kind, url, status, ByteRanges.CoveredBytes(l.Covered)));
        }

        return new BatchResponse(b.Id, BatchScope.Display(b.Label, b.Scope), b.Scope, b.CreatedAt, b.ExpiresAt, state,
            links.Count, links.Sum(l => l.Size), complete, views);
    }

    private QuotaView ToView(QuotaStatus q) =>
        new(q.Quota.Enabled, q.UsedBytes, q.Quota.VolumeBytes, q.Quota.PeriodDays, q.ActiveBatches, q.Quota.MaxActiveBatches, _quotas.FreesAt(UserId, q));
}
