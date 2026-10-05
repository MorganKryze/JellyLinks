using System.Net;
using System.Net.Sockets;
using System.Text;
using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.I18n;
using Jellyfin.Plugin.JellyLinks.Notify;
using Xunit;

namespace JellyLinks.Tests;

public class WebhookFormatterTests
{
    private static readonly LinkEvent Blocked =
        new(EventKind.BatchBlocked, Guid.Empty, "julien", "Severance · Saison 1", 7, Msg.Of("blocked", ("n", 4), ("limit", 3), ("ip", "4.4.4.4")));

    [Fact]
    public void No_url_means_no_request()
    {
        Assert.Null(WebhookFormatter.Build(new PluginConfiguration(), Blocked, "fr"));
    }

    [Fact]
    public void Unticked_event_means_no_request()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://ntfy.example/topic", NotifyBatchBlocked = false };
        Assert.Null(WebhookFormatter.Build(c, Blocked, "fr"));
    }

    [Fact]
    public async Task Ntfy_format_is_readable_text_with_title_and_tags()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://ntfy.example/topic", WebhookFormat = "ntfy" };
        var req = WebhookFormatter.Build(c, Blocked, "fr")!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.All(req.Headers.SelectMany(h => h.Value), v => Assert.True(Ascii.IsValid(v), v));
        Assert.Equal("JellyLinks : Lot bloqué", DecodeRfc2047(req.Headers.GetValues("Title").Single()));
        Assert.Contains("warning", req.Headers.GetValues("Tags").Single());
        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("julien", body);
        Assert.Contains("Severance · Saison 1", body);
        Assert.Contains("4 adresses distinctes (limite 3), dernière : 4.4.4.4", body);
    }

    [Fact]
    public async Task Json_format_carries_the_structured_event()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://hooks.example/x", WebhookFormat = "json" };
        var body = await WebhookFormatter.Build(c, Blocked, "fr")!.Content!.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"BatchBlocked\"", body);
        Assert.Contains("\"batchId\":7", body);
        Assert.Contains("\"detailCode\":\"blocked\"", body);
    }

    [Fact]
    public void English_server_gets_english_titles()
    {
        var c = new PluginConfiguration { WebhookUrl = "https://ntfy.example/topic", WebhookFormat = "ntfy" };
        Assert.Equal("JellyLinks: Batch blocked", WebhookFormatter.Build(c, Blocked, "en")!.Headers.GetValues("Title").Single());
    }

    [Fact]
    public async Task Ntfy_request_goes_through_a_real_http_client()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var buffer = new byte[8192];
            var head = new StringBuilder();
            while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buffer);
                if (n == 0)
                {
                    break;
                }

                head.Append(Encoding.ASCII.GetString(buffer, 0, n));
            }

            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
            return head.ToString();
        });

        try
        {
            var c = new PluginConfiguration { WebhookUrl = $"http://127.0.0.1:{port}/topic", WebhookFormat = "ntfy" };
            using var req = WebhookFormatter.Build(c, Blocked, "fr")!;
            using var client = new HttpClient();
            using var res = await client.SendAsync(req);
            Assert.True(res.IsSuccessStatusCode);
            Assert.Contains("Title: =?UTF-8?B?", await server, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string DecodeRfc2047(string value)
    {
        const string prefix = "=?UTF-8?B?";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith("?=", StringComparison.Ordinal))
        {
            return value;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(value[prefix.Length..^2]));
    }
}
