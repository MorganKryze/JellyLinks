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

    [Fact]
    public void The_dictionary_comes_first()
    {
        var script = ClientScript.Load(new[] { "core.js" });
        Assert.StartsWith("(window.JellyLinks = window.JellyLinks || {}).STRINGS = {", script, StringComparison.Ordinal);
        Assert.Contains("\"gen.title\": \"Liens de téléchargement\"", script, StringComparison.Ordinal);
        Assert.Contains("// JellyLinks client: core ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("// JellyLinks client: boot ", script, StringComparison.Ordinal);
    }
}
