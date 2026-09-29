using Jellyfin.Plugin.JellyLinks.Library;

namespace JellyLinks.Tests.Fakes;

public sealed class FakeLibraryGateway : ILibraryGateway
{
    public bool Allowed { get; set; } = true;
    public bool Exists { get; set; } = true;
    public string? Path { get; set; } = "/media/a.mkv";
    public List<ResolvedFile> Files { get; } = new();

    public bool CanDownload(Guid userId, Guid itemId) => Allowed;

    public bool ItemExists(Guid itemId) => Exists;

    public IReadOnlyList<ResolvedFile> Expand(Guid userId, IReadOnlyList<Guid> rootItemIds, bool allVersions, bool includeSubtitles) => Files;

    public string? ResolvePath(Guid userId, Guid itemId, string mediaSourceId, int? streamIndex) => Path;

    public string UserName(Guid userId) => "camille";
}
