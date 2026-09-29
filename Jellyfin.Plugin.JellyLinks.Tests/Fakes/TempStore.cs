using Jellyfin.Plugin.JellyLinks.Data;

namespace JellyLinks.Tests.Fakes;

/// <summary>A fresh database file per test, deleted afterwards.</summary>
public sealed class TempStore : IDisposable
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"jellylinks-{Guid.NewGuid():N}.db");

    public TempStore()
    {
        Store = new LinkStore(DbPath);
        Store.Migrate();
    }

    public LinkStore Store { get; }

    public static Selection AnySelection(Guid root) =>
        new(new[] { root }, false, true, Array.Empty<Guid>(), Array.Empty<string>());

    public static LinkRecord Video(Guid item, string name, long size) =>
        new(0, 0, item, item.ToString("N"), name, size, "video", null);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
        {
            File.Delete(f);
        }
    }
}
