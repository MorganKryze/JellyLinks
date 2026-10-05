using Jellyfin.Plugin.JellyLinks.I18n;
using Xunit;

namespace JellyLinks.Tests;

public class MsgTests
{
    [Fact]
    public void A_message_survives_storage()
    {
        var m = Msg.Parse(Msg.Of("new_ip", ("ip", "1.2.3.4"), ("n", 2), ("limit", 3)).Serialize());
        Assert.Equal("new_ip", m.Code);
        Assert.Equal("1.2.3.4", m.Args["ip"]);
        Assert.Equal("2", m.Args["n"]);
    }

    [Fact]
    public void Legacy_free_text_is_shown_as_is()
    {
        var m = Msg.Parse("adresse 1.1.1.1 (2/3)");
        Assert.Equal("text", m.Code);
        Assert.Equal("adresse 1.1.1.1 (2/3)", m.Render("en", "ev."));
        Assert.Equal("{ pas du json", Msg.Parse("{ pas du json").Render("fr", "ev."));
        Assert.Equal(string.Empty, Msg.Parse(null).Render("fr", "ev."));
    }

    [Fact]
    public void Rendering_formats_sizes_and_unlimited()
    {
        Assert.Equal("1,5 Go sur 500,0 Go en 7 jours",
            Msg.Of("quota", ("usedBytes", 1_500_000_000L), ("volumeBytes", 500_000_000_000L), ("n", 7)).Render("fr", "ev."));
        Assert.Equal("address 1.2.3.4 (2/∞)", Msg.Of("new_ip", ("ip", "1.2.3.4"), ("n", 2), ("limit", 0)).Render("en", "ev."));
        Assert.Equal("4 adresses distinctes (limite 3)", Msg.Of("ip_limit", ("n", 4), ("limit", 3)).Render("fr", "reason."));
    }
}
