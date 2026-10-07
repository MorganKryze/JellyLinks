using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Library;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Policy;
using Jellyfin.Plugin.JellyLinks.Signing;
using JellyLinks.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JellyLinks.Tests;

public sealed class FileGateTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly byte[] Key = new byte[32];
    private readonly TempStore _t = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
    private readonly FakeLibraryGateway _lib = new();
    private readonly FakeActivitySink _sink = new();
    private readonly PluginConfiguration _cfg = new();
    private readonly LinkSigner _signer = new(Key);
    private readonly FileGate _gate;
    private readonly long _batch;
    private readonly LinkRecord _link;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"jl-{Guid.NewGuid():N}.mkv");
    private string? _culture = "en-US";

    public FileGateTests()
    {
        File.WriteAllBytes(_file, new byte[1000]);
        _lib.Path = _file;
        var notifier = new Notifier(_sink, new NoHttp(), () => _cfg, NullLogger<Notifier>.Instance);
        _gate = new FileGate(_t.Store, _signer, _lib, new QuotaService(_t.Store, () => _cfg, _clock), notifier, () => _cfg, _clock,
            new ServerLanguage(() => _culture));
        var item = Guid.NewGuid();
        _batch = _t.Store.CreateBatch(User, 1_800_000_000, 1_800_000_000 + 3600, "Film", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "a.mkv", 1000) });
        _link = _t.Store.GetLinks(_batch)[0];
    }

    private string Token(long exp = 1_800_000_000 + 3600) => _signer.Sign(_link.Id, exp);

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no webhook in this test");
    }

    [Fact]
    public async Task Valid_link_is_served()
    {
        var r = await _gate.CheckAsync(Token(), "1.1.1.1", false);
        Assert.Equal(GateOutcome.Serve, r.Outcome);
        Assert.Equal(_file, r.Path);
    }

    [Fact]
    public async Task Bad_signature_is_404()
    {
        Assert.Equal(GateOutcome.NotFound, (await _gate.CheckAsync("garbage", "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task Expired_is_410_even_if_the_batch_row_says_active()
    {
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Theory]
    [InlineData(BatchStates.Revoked)]
    [InlineData(BatchStates.Blocked)]
    public async Task Revoked_or_blocked_is_403(string state)
    {
        _t.Store.SetBatchState(_batch, state, null);
        Assert.Equal(GateOutcome.Forbidden, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task Permission_removed_after_generation_is_403()
    {
        _lib.Allowed = false;
        Assert.Equal(GateOutcome.Forbidden, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task Item_removed_from_the_library_is_410()
    {
        _lib.Allowed = false;
        _lib.Exists = false;
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task Deleted_file_is_410()
    {
        _lib.Path = null;
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task Fourth_address_blocks_the_batch_and_notifies()
    {
        foreach (var ip in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
        {
            Assert.Equal(GateOutcome.Serve, (await _gate.CheckAsync(Token(), ip, false)).Outcome);
        }

        var fourth = await _gate.CheckAsync(Token(), "4.4.4.4", false);
        Assert.Equal(GateOutcome.Forbidden, fourth.Outcome);
        var blocked = _t.Store.GetBatch(_batch)!;
        Assert.Equal(BatchStates.Blocked, blocked.State);
        var reason = Msg.Parse(blocked.BlockedReason);
        Assert.Equal("ip_limit", reason.Code);
        Assert.Equal("4", reason.Args["n"]);
        Assert.DoesNotContain("4.4.4.4", blocked.BlockedReason!, StringComparison.Ordinal); // no address kept in the database
        Assert.Contains(_sink.Events, e => e.Kind == EventKind.BatchBlocked && e.Detail.Args["ip"] == "4.4.4.4");
        Assert.Equal(3, _sink.Events.Count(e => e.Kind == EventKind.NewIp));
    }

    [Fact]
    public async Task Ip_limit_zero_never_blocks()
    {
        _cfg.IpLimit = 0;
        for (var i = 1; i <= 10; i++)
        {
            var ip = $"10.0.0.{i}";
            Assert.Equal(GateOutcome.Serve, (await _gate.CheckAsync(Token(), ip, false)).Outcome);
        }
    }

    [Fact]
    public async Task Exhausted_quota_is_429()
    {
        _cfg.QuotaEnabled = true;
        _cfg.QuotaVolumeBytes = 100;
        _t.Store.AddUsage(User, 1_800_000_000 / 86_400, 100);
        Assert.Equal(GateOutcome.TooManyRequests, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
        Assert.Contains(_sink.Events, e => e.Kind == EventKind.QuotaReached);
    }

    [Fact]
    public async Task New_address_refused_by_quota_is_announced_once()
    {
        _cfg.QuotaEnabled = true;
        _cfg.QuotaVolumeBytes = 100;
        _t.Store.AddUsage(User, 1_800_000_000 / 86_400, 100);
        Assert.Equal(GateOutcome.TooManyRequests, (await _gate.CheckAsync(Token(), "5.5.5.5", false)).Outcome);
        Assert.Equal(GateOutcome.TooManyRequests, (await _gate.CheckAsync(Token(), "5.5.5.5", false)).Outcome);
        Assert.Single(_sink.Events, e => e.Kind == EventKind.NewIp);
        Assert.Contains(_sink.Events, e => e.Kind == EventKind.QuotaReached);
    }

    [Fact]
    public async Task Parallel_requests_from_one_new_address_announce_it_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => _gate.CheckAsync(Token(), "7.7.7.7", false))));
        Assert.All(results, r => Assert.Equal(GateOutcome.Serve, r.Outcome));
        Assert.Single(_sink.Events, e => e.Kind == EventKind.NewIp);
        Assert.Single(results, r => r.IsNewIp);
    }

    [Fact]
    public async Task Two_new_addresses_racing_for_the_last_slot_admit_only_one()
    {
        _cfg.IpLimit = 3;
        Assert.Equal(GateOutcome.Serve, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
        Assert.Equal(GateOutcome.Serve, (await _gate.CheckAsync(Token(), "2.2.2.2", false)).Outcome);

        var results = await Task.WhenAll(new[] { "3.3.3.3", "4.4.4.4" }.Select(ip => Task.Run(() => _gate.CheckAsync(Token(), ip, false))));

        Assert.Single(results, r => r.Outcome == GateOutcome.Serve);
        Assert.Single(results, r => r.Outcome == GateOutcome.Forbidden);
        Assert.Equal(BatchStates.Blocked, _t.Store.GetBatch(_batch)!.State);
    }

    [Fact]
    public async Task Head_skips_address_and_quota_checks()
    {
        _cfg.QuotaEnabled = true;
        _cfg.QuotaVolumeBytes = 1;
        _t.Store.AddUsage(User, 1_800_000_000 / 86_400, 100);
        var r = await _gate.CheckAsync(Token(), "9.9.9.9", true);
        Assert.Equal(GateOutcome.Serve, r.Outcome);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task A_renamed_file_is_served_and_the_link_follows_it()
    {
        var renamed = Path.Combine(Path.GetTempPath(), $"jl-{Guid.NewGuid():N}.mkv");
        File.WriteAllBytes(renamed, new byte[2000]);
        try
        {
            var newItem = Guid.NewGuid();
            _lib.MovedTo = new LocatedFile(renamed, newItem, newItem.ToString("N"), null);
            var r = await _gate.CheckAsync(Token(), "1.1.1.1", false);
            Assert.Equal(GateOutcome.Serve, r.Outcome);
            Assert.Equal(renamed, r.Path);
            Assert.Equal(newItem, r.Link!.ItemId);
            Assert.Equal(2000, _t.Store.GetLink(_link.Id)!.Size);
        }
        finally
        {
            File.Delete(renamed);
        }
    }

    [Fact]
    public async Task A_file_replaced_in_place_refreshes_the_link_size_and_resets_coverage()
    {
        _t.Store.AddCoverage(_link.Id, 0, 499, 1000); // half served against the size stored at creation
        File.WriteAllBytes(_file, new byte[3000]); // same path, same item id: only the size tells
        var r = await _gate.CheckAsync(Token(), "1.1.1.1", false);
        Assert.Equal(GateOutcome.Serve, r.Outcome);
        var stored = _t.Store.GetLink(_link.Id)!;
        Assert.Equal(3000, stored.Size);
        Assert.Equal(string.Empty, stored.Covered);
        Assert.False(stored.Complete);
        Assert.DoesNotContain(_sink.Events, e => e.Kind == EventKind.LinkMoved); // nothing was renamed
    }

    [Fact]
    public async Task An_ambiguous_rename_is_gone()
    {
        _lib.Path = null; // the gateway found zero or several candidates
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task A_file_that_vanishes_before_the_relink_is_gone()
    {
        var newItem = Guid.NewGuid();
        _lib.MovedTo = new LocatedFile(Path.Combine(Path.GetTempPath(), $"jl-{Guid.NewGuid():N}", "x.mkv"), newItem, newItem.ToString("N"), null);
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
        _lib.MovedTo = MovedTo(Path.Combine(Path.GetTempPath(), $"jl-{Guid.NewGuid():N}.mkv"));
        Assert.Equal(GateOutcome.Gone, (await _gate.CheckAsync(Token(), "1.1.1.1", false)).Outcome);
    }

    [Fact]
    public async Task An_alert_on_a_0_2_batch_carries_its_label_without_the_size_tail()
    {
        var item = Guid.NewGuid();
        var old = _t.Store.CreateBatch(User, 1_800_000_000, 1_800_000_000 + 3600, "Sample Film · 1 fichier · 0,4 Go", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "b.mkv", 1000) });
        await _gate.CheckAsync(_signer.Sign(_t.Store.GetLinks(old)[0].Id, 1_800_000_000 + 3600), "1.1.1.1", false);
        Assert.Equal("Sample Film", Assert.Single(_sink.Events, e => e.Kind == EventKind.NewIp).BatchLabel);
    }

    [Fact]
    public async Task An_alert_on_a_scoped_batch_carries_its_title_in_the_current_language()
    {
        var item = Guid.NewGuid();
        var scope = new BatchScope(new[] { new ScopeTitle("Andor", new[] { new ScopeSeason(2, new[] { 1 }) }) });
        var made = _t.Store.CreateBatch(User, 1_800_000_000, 1_800_000_000 + 3600, "Andor · Saison 2 · E01", TempStore.AnySelection(item),
            new[] { TempStore.Video(item, "c.mkv", 1000) }, scope);
        _culture = "en-US"; // stored in French, alerted after the server switched to English
        await _gate.CheckAsync(_signer.Sign(_t.Store.GetLinks(made)[0].Id, 1_800_000_000 + 3600), "1.1.1.1", false);
        Assert.Equal("Andor · Season 2 · E01", Assert.Single(_sink.Events, e => e.Kind == EventKind.NewIp).BatchLabel);
    }

    private static LocatedFile MovedTo(string path)
    {
        var item = Guid.NewGuid();
        return new LocatedFile(path, item, item.ToString("N"), null);
    }

    public void Dispose()
    {
        _t.Dispose();
        File.Delete(_file);
    }
}
