using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Library;

namespace JellyLinks.Tests.Fakes;

public sealed class FakeLibraryGateway : ILibraryGateway
{
    public bool Allowed { get; set; } = true;
    public bool Exists { get; set; } = true;
    public string? Path { get; set; } = "/media/a.mkv";

    /// <summary>Simulates a file found again under a new identity after a rename.</summary>
    public LocatedFile? MovedTo { get; set; }

    public List<ResolvedFile> Files { get; } = new();

    public Location Locate(Guid userId, LinkRecord link)
    {
        if (!Allowed)
        {
            return Exists ? Location.Forbidden : Location.Gone;
        }

        if (MovedTo is not null)
        {
            return Location.Found(MovedTo);
        }

        return Path is null ? Location.Gone : Location.Found(new LocatedFile(Path, link.ItemId, link.MediaSourceId, link.StreamIndex));
    }

    public IReadOnlyList<ResolvedFile> Expand(Guid userId, IReadOnlyList<Guid> rootItemIds, bool allVersions, bool includeSubtitles) => Files;

    public string UserName(Guid userId) => "camille";
}
