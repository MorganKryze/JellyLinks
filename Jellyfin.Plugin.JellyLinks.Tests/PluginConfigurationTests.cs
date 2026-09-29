using Jellyfin.Plugin.JellyLinks.Configuration;
using Xunit;

namespace JellyLinks.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var c = new PluginConfiguration();
        Assert.Equal(7, c.LinkValidityDays);
        Assert.Equal(3, c.IpLimit);
        Assert.False(c.QuotaEnabled);
        Assert.Equal(7, c.QuotaPeriodDays);
        Assert.Equal(0, c.QuotaMaxActiveBatches);
        Assert.Equal(90, c.RetentionDays);
        Assert.Equal("ntfy", c.WebhookFormat);
    }
}
