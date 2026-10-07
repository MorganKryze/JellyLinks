using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Library;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Policy;
using Jellyfin.Plugin.JellyLinks.Serving;
using Jellyfin.Plugin.JellyLinks.Signing;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyLinks.Api;

public sealed record NamedTop(string Name, long Bytes);

public sealed record OverviewView(OverviewStats Stats, IReadOnlyList<DayVolume> Days, IReadOnlyDictionary<string, string> UserNames,
    IReadOnlyList<NamedTop> TopTitles, IReadOnlyList<NamedTop> TopUsers);

public sealed record AdminBatch(BatchRow Batch, string UserName, Msg? Reason);

public sealed record EventView(long At, string Kind, Msg Detail);

public sealed record AdminBatchDetail(BatchRow Batch, string UserName, Msg? Reason, int IpLimit, IReadOnlyList<SessionRow> Sessions, IReadOnlyList<EventView> Events);

public sealed record ActivityEntry(long At, string Kind, Guid UserId, string UserName, long? BatchId, string? BatchLabel, BatchScope? BatchScope, Msg Detail);

public sealed record AdminUser(Guid Id, string Name, bool CanDownload, bool IsAdmin, bool HasOverride, EffectiveQuota Quota,
    long UsedBytes, int ActiveBatches, long ArchivedBytes, int ArchivedCompleted);

public sealed record QuotaOverrideBody(long VolumeBytes, int PeriodDays, int MaxActiveBatches);

public sealed record WebhookTestBody(string Url, string Format);

public sealed record WebhookTestResult(bool Ok, Msg Result);

public sealed record RevokeAllResult(int Revoked);

[ApiController]
[Route("JellyLinks/admin")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class AdminController : ControllerBase
{
    private readonly LinkStore _store;
    private readonly ILibraryGateway _library;
    private readonly QuotaService _quotas;
    private readonly Notifier _notifier;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _clock;
    private readonly SigningKey _key;
    private readonly ServerLanguage _language;

    public AdminController(LinkStore store, ILibraryGateway library, QuotaService quotas, Notifier notifier,
                           Func<PluginConfiguration> config, TimeProvider clock, SigningKey key, ServerLanguage language)
    {
        _store = store;
        _library = library;
        _quotas = quotas;
        _notifier = notifier;
        _config = config;
        _clock = clock;
        _key = key;
        _language = language;
    }

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    private Dictionary<Guid, string> Names() => _library.Users().ToDictionary(u => u.Id, u => u.Name);

    private static BatchRow Shown(BatchRow b) => b with { Label = BatchScope.Display(b.Label, b.Scope) };

    private string NameOf(Dictionary<Guid, string> names, Guid id) => names.TryGetValue(id, out var n) ? n : id == Guid.Empty ? Strings.T(_language.Current, "user.admin") : id.ToString("N");

    private static Msg? ReasonOf(BatchRow b) => b.BlockedReason is null ? null : Msg.Parse(b.BlockedReason);

    private IReadOnlyList<Guid>? UsersMatching(string? text, Dictionary<Guid, string> names) =>
        string.IsNullOrWhiteSpace(text) ? null
            : names.Where(kv => kv.Value.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Select(kv => kv.Key).ToList();

    private long? Since(int? days) => days is > 0 ? Now - (days.Value * 86_400L) : null;

    [HttpGet("overview")]
    public ActionResult<OverviewView> Overview()
    {
        var now = Now;
        var names = Names();
        var days = _store.DailyVolumes((now / 86_400) - 29);
        return new OverviewView(
            _store.GetOverview(now),
            days,
            // Jellyfin writes Guids without dashes ("N"): key the map the same way the page reads DayVolume.UserId.
            days.Select(d => d.UserId).Distinct().ToDictionary(id => id.ToString("N"), id => NameOf(names, id)),
            _store.TopTitles(now - (30 * 86_400L), 10).Select(t => new NamedTop(t.Key, t.Bytes)).ToList(),
            _store.TopUsers((now / 86_400) - 29, 10).Select(t => new NamedTop(NameOf(names, Guid.Parse(t.Key)), t.Bytes)).ToList());
    }

    [HttpGet("batches")]
    public ActionResult<IReadOnlyList<AdminBatch>> Batches([FromQuery] string? q, [FromQuery] string? state, [FromQuery] Guid? user, [FromQuery] int? days)
    {
        var names = Names();
        return _store.SearchBatches(new BatchQuery(q, UsersMatching(q, names), string.IsNullOrEmpty(state) ? null : state, user, Since(days)), Now)
            .Select(b => new AdminBatch(Shown(b), NameOf(names, b.UserId), ReasonOf(b))).ToList();
    }

    [HttpGet("batches/{id:long}")]
    public ActionResult<AdminBatchDetail> Batch(long id)
    {
        var row = _store.GetBatchRow(id, Now);
        if (row is null)
        {
            return NotFound();
        }

        var events = _store.ListEvents(new EventQuery(null, null, null, null, null, 1000)).Where(e => e.BatchId == id)
            .Select(e => new EventView(e.At, e.Kind, Msg.Parse(e.Detail))).ToList();
        return new AdminBatchDetail(Shown(row), NameOf(Names(), row.UserId), ReasonOf(row), row.IpLimitOverride ?? _config().IpLimit, _store.SessionsOfBatch(id), events);
    }

    [HttpGet("batches/{id:long}/links")]
    public ActionResult<IReadOnlyList<string>> Links(long id)
    {
        var b = _store.GetBatch(id);
        if (b is null)
        {
            return NotFound();
        }

        if (BatchStates.Effective(b.State, b.ExpiresAt, Now) != BatchStates.Active)
        {
            return Array.Empty<string>();
        }

        var signer = new LinkSigner(_key.Current);
        var baseUrl = LinkUrlBuilder.BaseUrl(_config().PublicBaseUrl, $"{Request.Scheme}://{Request.Host}{Request.PathBase}");
        return _store.GetLinks(id).Select(l => LinkUrlBuilder.Build(baseUrl, signer.Sign(l.Id, b.ExpiresAt), l.FileName)).ToList();
    }

    [HttpPost("batches/{id:long}/unblock")]
    public IActionResult Unblock(long id, [FromQuery] bool raise)
    {
        var b = _store.GetBatch(id);
        if (b is null)
        {
            return NotFound();
        }

        int? limit = null;
        if (raise)
        {
            var effective = b.IpLimitOverride ?? _config().IpLimit;
            if (effective != 0)
            {
                limit = Math.Max(effective, _store.GetBatchRow(id, Now)?.DistinctIps ?? 0) + 1;
            }
        }

        if (!_store.Unblock(id, limit))
        {
            return Conflict(Msg.Of("conflict"));
        }

        var detail = limit is int l ? Msg.Of("unblocked_raised", ("limit", l)) : Msg.Of("unblocked");

        _ = _notifier.PublishAsync(new LinkEvent(EventKind.BatchUnblocked, b.UserId, _library.UserName(b.UserId), BatchScope.Now(b, _language.Current), id, detail));
        return NoContent();
    }

    [HttpPost("batches/{id:long}/revoke")]
    public IActionResult Revoke(long id)
    {
        var b = _store.GetBatch(id);
        if (b is null)
        {
            return NotFound();
        }

        if (BatchStates.Effective(b.State, b.ExpiresAt, Now) is BatchStates.Active or BatchStates.Blocked)
        {
            _store.SetBatchState(id, BatchStates.Revoked, Msg.Of("revoked_admin").Serialize());
            _ = _notifier.PublishAsync(new LinkEvent(EventKind.BatchRevoked, b.UserId, _library.UserName(b.UserId), BatchScope.Now(b, _language.Current), id, Msg.Of("revoked_admin")));
        }

        return NoContent();
    }

    [HttpGet("activity")]
    public ActionResult<IReadOnlyList<ActivityEntry>> Activity([FromQuery] string? q, [FromQuery] string? kind, [FromQuery] Guid? user, [FromQuery] int? days)
    {
        var names = Names();
        var since = Since(days) ?? Now - (30 * 86_400L);
        var entries = new List<ActivityEntry>();
        if (kind is null or "" or "Session")
        {
            var batches = new Dictionary<long, BatchRecord?>();
            foreach (var s in _store.RecentSessions(since, 500))
            {
                if (!batches.TryGetValue(s.BatchId, out var b))
                {
                    b = batches[s.BatchId] = _store.GetBatch(s.BatchId);
                }

                if (b is null || (user is Guid u && b.UserId != u))
                {
                    continue;
                }

                var detail = Msg.Of("session", ("file", s.FileName), ("ip", s.Ip), ("ua", s.UserAgent),
                    ("coveredBytes", s.CoveredBytes), ("sizeBytes", s.Size), ("status", s.Status));
                var haystack = $"{s.FileName} {s.Ip} {s.UserAgent} {s.Status}";
                if (string.IsNullOrWhiteSpace(q) || haystack.Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || b.Label.Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || NameOf(names, b.UserId).Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    entries.Add(new ActivityEntry(s.LastAt, "Session", b.UserId, NameOf(names, b.UserId), b.Id,
                        BatchScope.Display(b.Label, b.Scope), b.Scope, detail));
                }
            }
        }

        if (kind != "Session")
        {
            entries.AddRange(_store.ListEvents(new EventQuery(q, string.IsNullOrEmpty(kind) ? null : kind, user, since, UsersMatching(q, names)))
                .Select(e => new ActivityEntry(e.At, e.Kind, e.UserId, NameOf(names, e.UserId), e.BatchId, e.BatchLabel is null ? null : BatchScope.Display(e.BatchLabel, e.BatchScope), e.BatchScope, Msg.Parse(e.Detail))));
        }

        return entries.OrderByDescending(e => e.At).Take(500).ToList();
    }

    [HttpGet("users")]
    public ActionResult<IReadOnlyList<AdminUser>> Users()
    {
        var archived = _store.ArchivedTotals().ToDictionary(t => t.UserId);
        return _library.Users().Select(u =>
        {
            var q = _quotas.GetStatus(u.Id);
            archived.TryGetValue(u.Id, out var a);
            return new AdminUser(u.Id, u.Name, u.CanDownload, u.IsAdmin, _store.GetQuotaOverride(u.Id) is not null, q.Quota,
                q.UsedBytes, q.ActiveBatches, a?.Bytes ?? 0, a?.Completed ?? 0);
        }).ToList();
    }

    [HttpPut("users/{id:guid}/quota")]
    public IActionResult SetQuota(Guid id, [FromBody] QuotaOverrideBody body)
    {
        var errors = new List<FieldError>();
        if (body.VolumeBytes < 0) { errors.Add(new(nameof(body.VolumeBytes), Msg.Of("min", ("min", 0)))); }
        if (body.PeriodDays is < 1 or > 365) { errors.Add(new(nameof(body.PeriodDays), Msg.Of("range", ("min", 1), ("max", 365)))); }
        if (body.MaxActiveBatches is < 0 or > 1000) { errors.Add(new(nameof(body.MaxActiveBatches), Msg.Of("range", ("min", 0), ("max", 1000)))); }
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        _store.SetQuotaOverride(new QuotaOverride(id, body.VolumeBytes, body.PeriodDays, body.MaxActiveBatches), id);
        return NoContent();
    }

    [HttpDelete("users/{id:guid}/quota")]
    public IActionResult ClearQuota(Guid id)
    {
        _store.SetQuotaOverride(null, id);
        return NoContent();
    }

    [HttpGet("settings")]
    public ActionResult<SettingsView> GetSettings() => SettingsRules.From(_config());

    [HttpPost("settings")]
    public IActionResult SaveSettings([FromBody] SettingsView body)
    {
        var errors = SettingsRules.Validate(body);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var plugin = Plugin.Instance!;
        SettingsRules.Apply(body, plugin.Configuration);
        plugin.SaveConfiguration();
        return NoContent();
    }

    [HttpPost("settings/test-webhook")]
    public async Task<ActionResult<WebhookTestResult>> TestWebhook([FromBody] WebhookTestBody body)
    {
        var (ok, result) = await _notifier.TestWebhookAsync(body.Url, body.Format).ConfigureAwait(false);
        return new WebhookTestResult(ok, result);
    }

    /// <summary>"Tout révoquer": every open batch is revoked (that is what kills the links); the new signing secret protects against a leaked one.</summary>
    [HttpPost("revoke-all")]
    public ActionResult<RevokeAllResult> RevokeAll()
    {
        var n = _store.RevokeAll(Msg.Of("all_revoked").Serialize(), Now);
        _key.Rotate();
        _ = _notifier.PublishAsync(new LinkEvent(EventKind.AllRevoked, Guid.Empty, Strings.T(_language.Current, "user.admin"), string.Empty, 0, Msg.Of("all_revoked", ("n", n))));
        return new RevokeAllResult(n);
    }
}
