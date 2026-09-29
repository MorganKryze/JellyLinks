using Jellyfin.Plugin.JellyLinks.Client;
using Xunit;

namespace JellyLinks.Tests;

public class ClientScriptTests
{
    [Fact]
    public void Every_part_is_embedded_and_concatenated_in_order()
    {
        var script = ClientScript.Load();
        var positions = ClientScript.Parts
            .Select(p => script.IndexOf($"// JellyLinks client: {Path.GetFileNameWithoutExtension(p)} ", StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Equal("boot.js", ClientScript.Parts[^1]);
    }
}
