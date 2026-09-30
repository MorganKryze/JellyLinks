using System.Security.Cryptography;

namespace Jellyfin.Plugin.JellyLinks.Signing;

/// <summary>
/// The HMAC key for link tokens, kept in its own file in the plugin data folder: never in the plugin configuration,
/// which Jellyfin serves to administrators and writes into backups.
/// </summary>
public sealed class SigningKey
{
    public const int Length = 32;

    private readonly string _path;
    private readonly object _gate = new();
    private byte[] _current;

    /// <param name="path">Key file.</param>
    /// <param name="legacyBase64">A key from an older configuration, imported once when no valid key file exists yet.</param>
    public SigningKey(string path, string? legacyBase64 = null)
    {
        _path = path;
        _current = Load() ?? Import(legacyBase64) ?? Generate();
    }

    public byte[] Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>A new random key: every link issued before stops verifying.</summary>
    public void Rotate()
    {
        lock (_gate)
        {
            _current = Generate();
        }
    }

    private byte[]? Load()
    {
        try
        {
            var key = File.ReadAllBytes(_path);
            return key.Length >= Length ? key : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private byte[]? Import(string? legacyBase64)
    {
        if (string.IsNullOrWhiteSpace(legacyBase64))
        {
            return null;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(legacyBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (key.Length < Length)
        {
            return null;
        }

        Write(key);
        return key;
    }

    private byte[] Generate()
    {
        var key = RandomNumberGenerator.GetBytes(Length);
        Write(key);
        return key;
    }

    private void Write(byte[] key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // private from the first byte
        }

        using (var stream = new FileStream(tmp, options))
        {
            stream.Write(key);
            stream.Flush(true); // fsync before the rename makes it the key
        }

        File.Move(tmp, _path, overwrite: true);
    }
}
