using Jellyfin.Plugin.JellyLinks.Configuration;

namespace Jellyfin.Plugin.JellyLinks.Api;

/// <summary>Every setting the admin panel edits. The signing secret is deliberately absent.</summary>
public sealed record SettingsView(
    int LinkValidityDays, int IpLimit, bool QuotaEnabled, long QuotaVolumeBytes, int QuotaPeriodDays, int QuotaMaxActiveBatches,
    int RetentionDays, string WebhookUrl, string WebhookFormat, bool NotifyBatchBlocked, bool NotifyNewIp, bool NotifyQuotaReached,
    bool NotifyBatchCompleted, string PublicBaseUrl);

public static class SettingsRules
{
    public static SettingsView From(PluginConfiguration c) => new(
        c.LinkValidityDays, c.IpLimit, c.QuotaEnabled, c.QuotaVolumeBytes, c.QuotaPeriodDays, c.QuotaMaxActiveBatches,
        c.RetentionDays, c.WebhookUrl, c.WebhookFormat, c.NotifyBatchBlocked, c.NotifyNewIp, c.NotifyQuotaReached,
        c.NotifyBatchCompleted, c.PublicBaseUrl);

    public static IReadOnlyList<string> Validate(SettingsView s)
    {
        var errors = new List<string>();
        void Range(int v, int min, int max, string what)
        {
            if (v < min || v > max)
            {
                errors.Add($"{what} : entre {min} et {max}.");
            }
        }

        Range(s.LinkValidityDays, 1, 365, "Validité (jours)");
        Range(s.IpLimit, 0, 100, "Limite d'adresses (0 = sans limite)");
        Range(s.QuotaPeriodDays, 1, 365, "Période du quota (jours)");
        Range(s.QuotaMaxActiveBatches, 0, 1000, "Lots actifs maximum (0 = illimité)");
        Range(s.RetentionDays, 7, 3650, "Rétention (jours)");
        if (s.QuotaVolumeBytes < 0)
        {
            errors.Add("Volume du quota : positif ou nul (0 = illimité).");
        }

        if (s.WebhookFormat is not ("ntfy" or "json"))
        {
            errors.Add("Format du webhook : ntfy ou json.");
        }

        if (!string.IsNullOrWhiteSpace(s.WebhookUrl) && !IsHttpUrl(s.WebhookUrl, plain: false))
        {
            errors.Add("Adresse du webhook : une URL http ou https.");
        }

        if (!string.IsNullOrWhiteSpace(s.PublicBaseUrl) && !IsHttpUrl(s.PublicBaseUrl, plain: true))
        {
            errors.Add("Adresse publique : une URL http ou https sans identifiants, paramètres ni ancre (ex. https://jellyfin.example).");
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
