using Jellyfin.Plugin.JellyLinks.Signing;
using Xunit;

namespace JellyLinks.Tests;

public class LinkSignerTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Round_trip_returns_id_and_expiry()
    {
        var s = new LinkSigner(Key);
        var token = s.Sign(42, 1_800_000_000);
        Assert.Equal(43, token.Length);
        Assert.True(s.TryVerify(token, out var id, out var exp));
        Assert.Equal(42, id);
        Assert.Equal(1_800_000_000, exp);
    }

    [Fact]
    public void One_altered_character_is_rejected()
    {
        var s = new LinkSigner(Key);
        var token = s.Sign(42, 1_800_000_000).ToCharArray();
        token[5] = token[5] == 'A' ? 'B' : 'A';
        Assert.False(s.TryVerify(new string(token), out _, out _));
    }

    [Fact]
    public void Another_key_rejects_the_token()
    {
        var token = new LinkSigner(Key).Sign(42, 1_800_000_000);
        var other = new LinkSigner(Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.False(other.TryVerify(token, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void Garbage_is_rejected_without_throwing(string token)
    {
        Assert.False(new LinkSigner(Key).TryVerify(token, out _, out _));
    }

    [Fact]
    public void A_key_shorter_than_32_bytes_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new LinkSigner(Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => new LinkSigner(new byte[31]));
    }
}
