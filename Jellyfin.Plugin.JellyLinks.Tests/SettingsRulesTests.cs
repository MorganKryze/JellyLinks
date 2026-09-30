using Jellyfin.Plugin.JellyLinks.Api;
using Jellyfin.Plugin.JellyLinks.Configuration;
using Xunit;

namespace JellyLinks.Tests;

public class SettingsRulesTests
{
    private static SettingsView Valid() => SettingsRules.From(new PluginConfiguration());

    [Fact]
    public void Defaults_are_valid_and_the_secret_is_never_exposed()
    {
        Assert.Empty(SettingsRules.Validate(Valid()));
        Assert.DoesNotContain(typeof(SettingsView).GetProperties(), p => p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("https://jellyfin.example", true)]
    [InlineData("https://jellyfin.example/jf/", true)]
    [InlineData("", true)]
    [InlineData("https://jellyfin.example/?x=1", false)]
    [InlineData("https://user:pw@jellyfin.example", false)]
    [InlineData("jellyfin.example", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Public_address_must_be_a_plain_http_url(string url, bool ok)
    {
        Assert.Equal(ok, SettingsRules.Validate(Valid() with { PublicBaseUrl = url }).Count == 0);
    }

    [Fact]
    public void Out_of_range_values_are_refused_with_a_message_each()
    {
        var bad = Valid() with { LinkValidityDays = 0, RetentionDays = 0, WebhookFormat = "xml", WebhookUrl = "ftp://x", IpLimit = -1 };
        Assert.Equal(5, SettingsRules.Validate(bad).Count);
    }

    [Fact]
    public void Apply_keeps_the_secret()
    {
        var c = new PluginConfiguration { SigningSecret = "keep-me" };
        SettingsRules.Apply(Valid() with { IpLimit = 5, PublicBaseUrl = " https://jellyfin.example " }, c);
        Assert.Equal(5, c.IpLimit);
        Assert.Equal("https://jellyfin.example", c.PublicBaseUrl);
        Assert.Equal("keep-me", c.SigningSecret);
    }
}
