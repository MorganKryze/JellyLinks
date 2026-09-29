using System.Security.Cryptography;
using Jellyfin.Plugin.JellyLinks.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyLinks;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Directory.CreateDirectory(DataFolderPath);
        DatabasePath = Path.Combine(DataFolderPath, "jellylinks.db");

        if (string.IsNullOrEmpty(Configuration.SigningSecret))
        {
            Configuration.SigningSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            SaveConfiguration();
        }
    }

    public static Plugin? Instance { get; private set; }

    public string DatabasePath { get; }

    public override string Name => "JellyLinks";

    public override Guid Id => Guid.Parse("5b0f6e0c-2d4b-4f5e-9a57-3c1d8e7a4b21");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        };
    }
}
