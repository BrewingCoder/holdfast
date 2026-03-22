using System.Text.Json;
using HoldFast.Domain.Entities;
using HotChocolate;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private.Types;

// ── JSON helper ───────────────────────────────────────────────────────────────
// The legacy Go backend stored multi-value alert fields as JSON arrays in text
// columns. These extension classes parse those columns into typed GraphQL lists.

file static class AlertJsonHelper
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static List<T> ParseList<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(json, Opts) ?? []; }
        catch { return []; }
    }
}

// ── ErrorAlert ────────────────────────────────────────────────────────────────
// The Go gqlgen schema exposes most ErrorAlert fields in PascalCase.
// HC's SnakeCaseNamingConventions would rename them to snake_case, so we
// add explicit PascalCase GraphQL names via type extension methods.

[ExtendObjectType(typeof(ErrorAlert))]
public class ErrorAlertTypeExtension
{
    // Rename: HC exposes these as snake_case but frontend queries PascalCase
    [GraphQLName("Name")]
    public string? GetName([Parent] ErrorAlert a) => a.Name;

    [GraphQLName("Query")]
    public string? GetQuery([Parent] ErrorAlert a) => a.Query;

    [GraphQLName("Type")]
    public string GetType([Parent] ErrorAlert a) => "error_alert";

    [GraphQLName("CountThreshold")]
    public int GetCountThreshold([Parent] ErrorAlert a) => a.CountThreshold ?? 0;

    [GraphQLName("ThresholdWindow")]
    public int? GetThresholdWindow([Parent] ErrorAlert a) => a.ThresholdWindow;

    [GraphQLName("Frequency")]
    public int GetFrequency([Parent] ErrorAlert a) => a.Frequency ?? 0;

    [GraphQLName("LastAdminToEditID")]
    public int? GetLastAdminToEditId([Parent] ErrorAlert a) => a.LastAdminToEditId;

    // Typed replacements for raw JSON string columns
    [GraphQLName("ChannelsToNotify")]
    public List<SanitizedSlackChannel> GetChannelsToNotify([Parent] ErrorAlert a)
        => AlertJsonHelper.ParseList<SanitizedSlackChannel>(a.ChannelsToNotify);

    [GraphQLName("EmailsToNotify")]
    public List<string> GetEmailsToNotify([Parent] ErrorAlert a)
        => AlertJsonHelper.ParseList<string>(a.EmailsToNotify);

    [GraphQLName("WebhookDestinations")]
    public List<WebhookDestinationGql> GetWebhookDestinations([Parent] ErrorAlert a)
        => AlertJsonHelper.ParseList<WebhookDestinationGql>(a.WebhookDestinations);

    [GraphQLName("RegexGroups")]
    public List<string> GetRegexGroups([Parent] ErrorAlert a)
        => AlertJsonHelper.ParseList<string>(a.RegexGroups);

    // Fields absent from entity — return stubs so schema validates
    [GraphQLName("DailyFrequency")]
    public List<long> GetDailyFrequency([Parent] ErrorAlert a) => [];

    [GraphQLName("DiscordChannelsToNotify")]
    public List<DiscordChannelInfo> GetDiscordChannelsToNotify([Parent] ErrorAlert a) => [];

    [GraphQLName("MicrosoftTeamsChannelsToNotify")]
    public List<MicrosoftTeamsChannelInfo> GetMicrosoftTeamsChannelsToNotify([Parent] ErrorAlert a) => [];

    [GraphQLName("default")]
    public bool GetDefault([Parent] ErrorAlert a) => false;
}

// ── SessionAlert ──────────────────────────────────────────────────────────────

[ExtendObjectType(typeof(SessionAlert))]
public class SessionAlertTypeExtension
{
    [GraphQLName("Name")]
    public string? GetName([Parent] SessionAlert a) => a.Name;

    [GraphQLName("Type")]
    public string GetType([Parent] SessionAlert a) => a.Type ?? "session_alert";

    [GraphQLName("CountThreshold")]
    public int GetCountThreshold([Parent] SessionAlert a) => a.CountThreshold ?? 0;

    [GraphQLName("ThresholdWindow")]
    public int? GetThresholdWindow([Parent] SessionAlert a) => a.ThresholdWindow;

    [GraphQLName("LastAdminToEditID")]
    public int? GetLastAdminToEditId([Parent] SessionAlert a) => a.LastAdminToEditId;

    [GraphQLName("ChannelsToNotify")]
    public List<SanitizedSlackChannel> GetChannelsToNotify([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<SanitizedSlackChannel>(a.ChannelsToNotify);

    [GraphQLName("EmailsToNotify")]
    public List<string> GetEmailsToNotify([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<string>(a.EmailsToNotify);

    [GraphQLName("WebhookDestinations")]
    public List<WebhookDestinationGql> GetWebhookDestinations([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<WebhookDestinationGql>(a.WebhookDestinations);

    [GraphQLName("ExcludeRules")]
    public List<string> GetExcludeRules([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<string>(a.ExcludeRules);

    [GraphQLName("TrackProperties")]
    public List<TrackProperty> GetTrackProperties([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<TrackProperty>(a.TrackProperties);

    [GraphQLName("UserProperties")]
    public List<UserProperty> GetUserProperties([Parent] SessionAlert a)
        => AlertJsonHelper.ParseList<UserProperty>(a.UserProperties);

    [GraphQLName("DailyFrequency")]
    public List<long> GetDailyFrequency([Parent] SessionAlert a) => [];

    [GraphQLName("DiscordChannelsToNotify")]
    public List<DiscordChannelInfo> GetDiscordChannelsToNotify([Parent] SessionAlert a) => [];

    [GraphQLName("MicrosoftTeamsChannelsToNotify")]
    public List<MicrosoftTeamsChannelInfo> GetMicrosoftTeamsChannelsToNotify([Parent] SessionAlert a) => [];

    [GraphQLName("default")]
    public bool GetDefault([Parent] SessionAlert a) => false;

    [GraphQLName("ExcludedEnvironments")]
    public List<string> GetExcludedEnvironments([Parent] SessionAlert a) => [];
}

// ── LogAlert ──────────────────────────────────────────────────────────────────

[ExtendObjectType(typeof(LogAlert))]
public class LogAlertTypeExtension
{
    [GraphQLName("Name")]
    public string GetName([Parent] LogAlert a) => a.Name ?? "";

    [GraphQLName("Type")]
    public string GetType([Parent] LogAlert a) => "log_alert";

    [GraphQLName("CountThreshold")]
    public int GetCountThreshold([Parent] LogAlert a) => a.CountThreshold ?? 0;

    [GraphQLName("ThresholdWindow")]
    public int GetThresholdWindow([Parent] LogAlert a) => a.ThresholdWindow ?? 0;

    [GraphQLName("LastAdminToEditID")]
    public int? GetLastAdminToEditId([Parent] LogAlert a) => a.LastAdminToEditId;

    [GraphQLName("ChannelsToNotify")]
    public List<SanitizedSlackChannel> GetChannelsToNotify([Parent] LogAlert a)
        => AlertJsonHelper.ParseList<SanitizedSlackChannel>(a.ChannelsToNotify);

    [GraphQLName("EmailsToNotify")]
    public List<string> GetEmailsToNotify([Parent] LogAlert a)
        => AlertJsonHelper.ParseList<string>(a.EmailsToNotify);

    [GraphQLName("DailyFrequency")]
    public List<long> GetDailyFrequency([Parent] LogAlert a) => [];

    [GraphQLName("DiscordChannelsToNotify")]
    public List<DiscordChannelInfo> GetDiscordChannelsToNotify([Parent] LogAlert a) => [];

    [GraphQLName("MicrosoftTeamsChannelsToNotify")]
    public List<MicrosoftTeamsChannelInfo> GetMicrosoftTeamsChannelsToNotify([Parent] LogAlert a) => [];

    [GraphQLName("default")]
    public bool GetDefault([Parent] LogAlert a) => false;
}

// ── MetricMonitor ─────────────────────────────────────────────────────────────
// MetricMonitor uses snake_case field names in the Go schema (unlike legacy
// alert types). HC produces the right names automatically; we only need typed
// replacements for JSON string columns and stubs for missing fields.

[ExtendObjectType(typeof(MetricMonitor))]
public class MetricMonitorTypeExtension
{
    [GraphQLName("channels_to_notify")]
    public List<SanitizedSlackChannel> GetChannelsToNotify([Parent] MetricMonitor m)
        => AlertJsonHelper.ParseList<SanitizedSlackChannel>(m.ChannelsToNotify);

    [GraphQLName("emails_to_notify")]
    public List<string> GetEmailsToNotify([Parent] MetricMonitor m)
        => AlertJsonHelper.ParseList<string>(m.EmailsToNotify);

    [GraphQLName("webhook_destinations")]
    public List<WebhookDestinationGql> GetWebhookDestinations([Parent] MetricMonitor m)
        => AlertJsonHelper.ParseList<WebhookDestinationGql>(m.WebhookDestinations);

    [GraphQLName("discord_channels_to_notify")]
    public List<DiscordChannelInfo> GetDiscordChannelsToNotify([Parent] MetricMonitor m) => [];

    [GraphQLName("filters")]
    public List<MetricMonitorFilter> GetFilters([Parent] MetricMonitor m)
        => AlertJsonHelper.ParseList<MetricMonitorFilter>(m.Filters);

    [GraphQLName("period_minutes")]
    public int? GetPeriodMinutes([Parent] MetricMonitor m) => null;
}
