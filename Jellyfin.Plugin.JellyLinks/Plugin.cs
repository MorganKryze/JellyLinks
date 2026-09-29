using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.JellyLinks.Client;
using Jellyfin.Plugin.JellyLinks.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyLinks;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private const int MaxAttempts = 4;
    private readonly ILogger<Plugin> _log;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> log)
        : base(applicationPaths, xmlSerializer)
    {
        _log = log;
        Instance = this;
        Directory.CreateDirectory(DataFolderPath);
        DatabasePath = Path.Combine(DataFolderPath, "jellylinks.db");

        if (string.IsNullOrEmpty(Configuration.SigningSecret))
        {
            Configuration.SigningSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            SaveConfiguration();
        }

        RegisterClientScript(1);
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

    /// <summary>
    /// Hands the web client script to JavaScript Injector (n00bcodr) through its public RegisterScript(JObject).
    /// The JObject is built by reflection from JSON, so this plugin needs no Newtonsoft reference.
    /// JavaScript Injector may load or become ready after us: retry a few times, never fail the server.
    /// </summary>
    private void RegisterClientScript(int attempt)
    {
        try
        {
            var register = AssemblyLoadContext.All
                .SelectMany(c => c.Assemblies)
                .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.JavaScriptInjector")
                ?.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface")
                ?.GetMethod("RegisterScript");
            if (register is null)
            {
                Retry(attempt, "JavaScript Injector not found");
                return;
            }

            var json = JsonSerializer.Serialize(new
            {
                id = $"{Id}-client",
                name = "JellyLinks",
                script = ClientScript.Load(),
                enabled = true,
                requiresAuthentication = true,
                pluginId = Id.ToString(),
                pluginName = Name,
                pluginVersion = Version?.ToString() ?? "0.0.0",
            });
            var payloadType = register.GetParameters()[0].ParameterType;
            var payload = payloadType.GetMethod("Parse", new[] { typeof(string) })!.Invoke(null, new object[] { json });
            register.Invoke(null, new[] { payload });
            _log.LogInformation("[JellyLinks] client script registered with JavaScript Injector");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            Retry(attempt, "JavaScript Injector not ready");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[JellyLinks] could not register the client script: the Links button will not appear");
        }
    }

    private void Retry(int attempt, string why)
    {
        if (attempt >= MaxAttempts)
        {
            _log.LogWarning("[JellyLinks] {Why}: the Links button will not appear (install JavaScript Injector)", why);
            return;
        }

        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => RegisterClientScript(attempt + 1), TaskScheduler.Default);
    }
}
