using Jellyfin.Plugin.JellyLinks.Configuration;
using Jellyfin.Plugin.JellyLinks.I18n;

namespace Jellyfin.Plugin.JellyLinks.Api;

/// <summary>Every setting the admin panel edits. The signing secret is deliberately absent.</summary>
public sealed record SettingsView(
    int LinkValidityDays, int IpLimit, bool QuotaEnabled, long QuotaVolumeBytes, int QuotaPeriodDays, int QuotaMaxActiveBatches,
    int RetentionDays, string WebhookUrl, string WebhookFormat, bool NotifyBatchBlocked, bool NotifyNewIp, bool NotifyQuotaReached,
    bool NotifyBatchCompleted, string PublicBaseUrl);

public sealed record FieldError(string Field, Msg Error);

public static class SettingsRules
{
    public static SettingsView From(PluginConfiguration c) => new(
        c.LinkValidityDays, c.IpLimit, c.QuotaEnabled, c.QuotaVolumeBytes, c.QuotaPeriodDays, c.QuotaMaxActiveBatches,
        c.RetentionDays, c.WebhookUrl, c.WebhookFormat, c.NotifyBatchBlocked, c.NotifyNewIp, c.NotifyQuotaReached,
        c.NotifyBatchCompleted, c.PublicBaseUrl);

    public static IReadOnlyList<FieldError> Validate(SettingsView s)
    {
        var missing = new List<FieldError>();
        if (s.WebhookUrl is null) { missing.Add(new(nameof(SettingsView.WebhookUrl), Msg.Of("required"))); }
        if (s.WebhookFormat is null) { missing.Add(new(nameof(SettingsView.WebhookFormat), Msg.Of("required"))); }
        if (s.PublicBaseUrl is null) { missing.Add(new(nameof(SettingsView.PublicBaseUrl), Msg.Of("required"))); }
        if (missing.Count > 0)
        {
            return missing;
        }

        var errors = new List<FieldError>();
        void Range(int v, int min, int max, string field)
        {
            if (v < min || v > max)
            {
                errors.Add(new FieldError(field, Msg.Of("range", ("min", min), ("max", max))));
            }
        }

        Range(s.LinkValidityDays, 1, 365, nameof(SettingsView.LinkValidityDays));
        Range(s.IpLimit, 0, 100, nameof(SettingsView.IpLimit));
        Range(s.QuotaPeriodDays, 1, 365, nameof(SettingsView.QuotaPeriodDays));
        Range(s.QuotaMaxActiveBatches, 0, 1000, nameof(SettingsView.QuotaMaxActiveBatches));
        Range(s.RetentionDays, 7, 3650, nameof(SettingsView.RetentionDays));
        if (s.QuotaVolumeBytes < 0)
        {
            errors.Add(new FieldError(nameof(SettingsView.QuotaVolumeBytes), Msg.Of("min", ("min", 0))));
        }

        if (s.WebhookFormat is not ("ntfy" or "json"))
        {
            errors.Add(new FieldError(nameof(SettingsView.WebhookFormat), Msg.Of("format")));
        }

        if (!string.IsNullOrWhiteSpace(s.WebhookUrl) && !IsHttpUrl(s.WebhookUrl, plain: false))
        {
            errors.Add(new FieldError(nameof(SettingsView.WebhookUrl), Msg.Of("url")));
        }

        if (!string.IsNullOrWhiteSpace(s.PublicBaseUrl) && !IsHttpUrl(s.PublicBaseUrl, plain: true))
        {
            errors.Add(new FieldError(nameof(SettingsView.PublicBaseUrl), Msg.Of("plainUrl")));
        }

        return errors;
    }

    public static void Apply(SettingsView s, PluginConfiguration c)
    {
        c.LinkValidityDays = s.LinkValidityDays;
        c.IpLimit = s.IpLimit;
        c.QuotaEnabled = s.QuotaEnabled;
        c.QuotaVolumeBytes = s.QuotaVolumeBytes;
        c.QuotaPeriodDays = s.QuotaPeriodDays;
        c.QuotaMaxActiveBatches = s.QuotaMaxActiveBatches;
        c.RetentionDays = s.RetentionDays;
        c.WebhookUrl = s.WebhookUrl.Trim();
        c.WebhookFormat = s.WebhookFormat;
        c.NotifyBatchBlocked = s.NotifyBatchBlocked;
        c.NotifyNewIp = s.NotifyNewIp;
        c.NotifyQuotaReached = s.NotifyQuotaReached;
        c.NotifyBatchCompleted = s.NotifyBatchCompleted;
        c.PublicBaseUrl = s.PublicBaseUrl.Trim();
    }

    private static bool IsHttpUrl(string value, bool plain) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
        && (!plain || (u.UserInfo.Length == 0 && u.Query.Length == 0 && u.Fragment.Length == 0));
}
