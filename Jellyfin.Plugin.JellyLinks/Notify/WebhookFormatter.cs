using System.Net.Http.Json;
using System.Text;
using Jellyfin.Plugin.JellyLinks.Configuration;

namespace Jellyfin.Plugin.JellyLinks.Notify;

public static class WebhookFormatter
{
    public static HttpRequestMessage? Build(PluginConfiguration c, LinkEvent e)
    {
        if (string.IsNullOrWhiteSpace(c.WebhookUrl) || !IsEnabled(c, e.Kind))
        {
            return null;
        }

        var req = new HttpRequestMessage(HttpMethod.Post, c.WebhookUrl);
        if (string.Equals(c.WebhookFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            req.Content = JsonContent.Create(new
            {
                kind = e.Kind.ToString(),
                userId = e.UserId,
                userName = e.UserName,
                batchId = e.BatchId,
                batchLabel = e.BatchLabel,
                detail = e.Detail,
            });
            return req;
        }

        req.Headers.Add("Title", HeaderValue($"JellyLinks : {Title(e.Kind)}"));
        req.Headers.Add("Tags", Tags(e.Kind));
        req.Content = new StringContent($"{e.UserName} · {e.BatchLabel}\n{e.Detail}", Encoding.UTF8, "text/plain");
        return req;
    }

    /// <summary>HTTP headers are ASCII only: anything else goes as RFC 2047, which ntfy decodes.</summary>
    internal static string HeaderValue(string value) =>
        Ascii.IsValid(value) ? value : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private static bool IsEnabled(PluginConfiguration c, EventKind k) => k switch
    {
        EventKind.BatchBlocked => c.NotifyBatchBlocked,
        EventKind.NewIp => c.NotifyNewIp,
        EventKind.QuotaReached => c.NotifyQuotaReached,
        EventKind.BatchCompleted => c.NotifyBatchCompleted,
        _ => false,
    };

    internal static string Title(EventKind k) => k switch
    {
        EventKind.BatchBlocked => "lot bloqué",
        EventKind.NewIp => "nouvelle adresse",
        EventKind.QuotaReached => "quota atteint",
        EventKind.BatchCompleted => "lot téléchargé",
        _ => k.ToString(),
    };

    private static string Tags(EventKind k) => k switch
    {
        EventKind.BatchBlocked => "warning,no_entry",
        EventKind.NewIp => "warning,globe_with_meridians",
        EventKind.QuotaReached => "hourglass",
        _ => "white_check_mark",
    };
}
