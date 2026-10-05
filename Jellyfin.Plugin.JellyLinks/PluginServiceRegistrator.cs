using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.Data;
using Jellyfin.Plugin.JellyLinks.Library;
using Jellyfin.Plugin.JellyLinks.Maintenance;
using Jellyfin.Plugin.JellyLinks.Notify;
using Jellyfin.Plugin.JellyLinks.Policy;
using Jellyfin.Plugin.JellyLinks.Signing;
using Jellyfin.Plugin.JellyLinks.Tracking;
using Jellyfin.Plugin.JellyLinks.I18n;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyLinks;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new ServerLanguage(() => sp.GetRequiredService<IServerConfigurationManager>().Configuration.UICulture));
        services.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
        services.AddSingleton(_ =>
        {
            var store = new LinkStore(Plugin.Instance!.DatabasePath);
            store.Migrate();
            return store;
        });
        services.AddSingleton(_ => Plugin.Instance!.SigningKey);
        services.AddTransient(sp => new LinkSigner(sp.GetRequiredService<SigningKey>().Current));
        services.AddSingleton<ILibraryGateway, JellyfinLibraryGateway>();
        services.AddSingleton<IActivitySink, JellyfinActivitySink>();
        services.AddSingleton<Notifier>();
        services.AddSingleton<QuotaService>();
        services.AddSingleton<MaintenanceRunner>();
        services.AddSingleton<SessionTracker>();
        services.AddTransient<FileGate>();
    }
}
