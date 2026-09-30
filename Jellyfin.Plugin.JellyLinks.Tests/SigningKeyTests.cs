using Jellyfin.Plugin.JellyLinks.Signing;
using Xunit;

namespace JellyLinks.Tests;

public sealed class SigningKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"jl-key-{Guid.NewGuid():N}");

    private string KeyPath => Path.Combine(_dir, "signing.key");

    [Fact]
    public void A_new_key_is_created_once_and_reused()
    {
        var first = new SigningKey(KeyPath).Current;
        Assert.Equal(SigningKey.Length, first.Length);
        Assert.Equal(first, File.ReadAllBytes(KeyPath));
        Assert.Equal(first, new SigningKey(KeyPath).Current);
    }

    [Fact]
    public void Legacy_secret_is_imported_when_no_file()
    {
        var legacy = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var key = new SigningKey(KeyPath, Convert.ToBase64String(legacy));
        Assert.Equal(legacy, key.Current);
        Assert.Equal(legacy, File.ReadAllBytes(KeyPath));
    }

    [Fact]
    public void The_file_wins_over_a_legacy_secret()
    {
        var onDisk = new SigningKey(KeyPath).Current;
        var legacy = Convert.ToBase64String(new byte[32]);
        Assert.Equal(onDisk, new SigningKey(KeyPath, legacy).Current);
    }

    [Fact]
    public void Short_or_garbage_material_is_replaced_by_a_fresh_key()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(KeyPath, new byte[5]);
        var key = new SigningKey(KeyPath, "not base64 !");
        Assert.Equal(SigningKey.Length, key.Current.Length);
        Assert.NotEqual(new byte[SigningKey.Length], key.Current);
    }

    [Fact]
    public void Rotate_changes_the_key_and_persists_it()
    {
        var key = new SigningKey(KeyPath);
        var before = key.Current;
        key.Rotate();
        Assert.NotEqual(before, key.Current);
        Assert.Equal(key.Current, new SigningKey(KeyPath).Current);
    }

    [Fact]
    public void The_key_file_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        _ = new SigningKey(KeyPath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
