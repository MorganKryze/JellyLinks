using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
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

    public FileGateTests()
    {
        File.WriteAllBytes(_file, new byte[1000]);
        _lib.Path = _file;
        var notifier = new Notifier(_sink, new NoHttp(), () => _cfg, NullLogger<Notifier>.Instance);
        _gate = new FileGate(_t.Store, _signer, _lib, new QuotaService(_t.Store, () => _cfg, _clock), notifier, () => _cfg, _clock);
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
            var ok = await _gate.CheckAsync(Token(), ip, false);
            Assert.Equal(GateOutcome.Serve, ok.Outcome);
            _t.Store.InsertSession(new SessionRecord(0, _link.Id, ip, "jd", 1, 1, 0, "", SessionStatuses.InProgress, ok.IsNewIp));
        }

        var fourth = await _gate.CheckAsync(Token(), "4.4.4.4", false);
        Assert.Equal(GateOutcome.Forbidden, fourth.Outcome);
        Assert.Equal(BatchStates.Blocked, _t.Store.GetBatch(_batch)!.State);
        Assert.Contains(_sink.Events, e => e.Kind == EventKind.BatchBlocked);
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
            _t.Store.InsertSession(new SessionRecord(0, _link.Id, ip, "jd", 1, 1, 0, "", SessionStatuses.InProgress, true));
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
    public async Task Refused_new_address_on_exhausted_quota_emits_no_new_ip_event()
    {
        _cfg.QuotaEnabled = true;
        _cfg.QuotaVolumeBytes = 100;
        _t.Store.AddUsage(User, 1_800_000_000 / 86_400, 100);
        Assert.Equal(GateOutcome.TooManyRequests, (await _gate.CheckAsync(Token(), "5.5.5.5", false)).Outcome);
        Assert.DoesNotContain(_sink.Events, e => e.Kind == EventKind.NewIp);
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

    public void Dispose()
    {
        _t.Dispose();
        File.Delete(_file);
    }
}
