using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.JellyLinks.Signing;

/// <summary>Issues and checks opaque link tokens: id + expiry + truncated HMAC-SHA256.</summary>
public sealed class LinkSigner
{
    private const int MacLength = 16;
    private const int TokenBytes = 8 + 8 + MacLength;
    private readonly byte[] _key;

    public LinkSigner(byte[] key) => _key = key;

    public string Sign(long linkId, long expiresUnix)
    {
        Span<byte> buf = stackalloc byte[TokenBytes];
        BinaryPrimitives.WriteInt64BigEndian(buf[..8], linkId);
        BinaryPrimitives.WriteInt64BigEndian(buf.Slice(8, 8), expiresUnix);
        Mac(buf[..16]).AsSpan(0, MacLength).CopyTo(buf[16..]);
        return Base64UrlEncode(buf);
    }

    public bool TryVerify(string token, out long linkId, out long expiresUnix)
    {
        linkId = 0;
        expiresUnix = 0;
        var buf = Base64UrlDecode(token);
        if (buf is null || buf.Length != TokenBytes)
        {
            return false;
        }

        var expected = Mac(buf.AsSpan(0, 16)).AsSpan(0, MacLength);
        if (!CryptographicOperations.FixedTimeEquals(expected, buf.AsSpan(16)))
        {
            return false;
        }

        linkId = BinaryPrimitives.ReadInt64BigEndian(buf.AsSpan(0, 8));
        expiresUnix = BinaryPrimitives.ReadInt64BigEndian(buf.AsSpan(8, 8));
        return true;
    }

    private byte[] Mac(ReadOnlySpan<byte> data) => HMACSHA256.HashData(_key, data);

    private static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        var b64 = s.Replace('-', '+').Replace('_', '/');
        b64 += new string('=', (4 - (b64.Length % 4)) % 4);
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
