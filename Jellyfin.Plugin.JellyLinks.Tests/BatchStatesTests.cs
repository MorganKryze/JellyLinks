using Jellyfin.Plugin.JellyLinks.Data;
using Xunit;

namespace JellyLinks.Tests;

public class BatchStatesTests
{
    [Theory]
    [InlineData(BatchStates.Active, 100, 99, BatchStates.Active)]
    [InlineData(BatchStates.Active, 100, 100, BatchStates.Expired)]
    [InlineData(BatchStates.Blocked, 100, 200, BatchStates.Blocked)]
    [InlineData(BatchStates.Blocked, 100, 50, BatchStates.Blocked)]
    [InlineData(BatchStates.Revoked, 100, 200, BatchStates.Revoked)]
    [InlineData(BatchStates.Expired, 100, 50, BatchStates.Expired)]
    public void A_batch_past_its_expiry_reads_as_expired_before_maintenance_runs(string stored, long expiresAt, long now, string expected)
    {
        Assert.Equal(expected, BatchStates.Effective(stored, expiresAt, now));
    }
}
