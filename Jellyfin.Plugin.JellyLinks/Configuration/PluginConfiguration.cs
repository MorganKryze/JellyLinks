using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyLinks.Configuration;

/// <summary>Global settings, serialised to XML by Jellyfin.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public int LinkValidityDays { get; set; } = 7;

    /// <summary>Distinct client addresses allowed per batch. 0 disables the limit.</summary>
    public int IpLimit { get; set; } = 3;

    public bool QuotaEnabled { get; set; }
    public long QuotaVolumeBytes { get; set; } = 500L * 1000 * 1000 * 1000;
    public int QuotaPeriodDays { get; set; } = 7;

    /// <summary>Active batches allowed per user. 0 means unlimited.</summary>
    public int QuotaMaxActiveBatches { get; set; }

    public int RetentionDays { get; set; } = 90;

    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>"ntfy" or "json".</summary>
    public string WebhookFormat { get; set; } = "ntfy";

    public bool NotifyBatchBlocked { get; set; } = true;
    public bool NotifyNewIp { get; set; } = true;
    public bool NotifyQuotaReached { get; set; } = true;
    public bool NotifyBatchCompleted { get; set; }

    /// <summary>Base64 HMAC key. Generated on first start; rotating it revokes every link.</summary>
    public string SigningSecret { get; set; } = string.Empty;
}
