namespace HoldFast.Shared.Notifications;

/// <summary>
/// Sends alert notifications to external platforms (Slack, Discord, Teams, webhooks).
/// All methods are fire-and-forget safe: they log errors but never throw.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Post a message to a Slack channel using the Slack Web API.
    /// </summary>
    Task SendSlackMessageAsync(string accessToken, string channelId, SlackMessage message, CancellationToken ct);

    /// <summary>
    /// Post a message to a Discord channel via incoming webhook URL.
    /// </summary>
    Task SendDiscordMessageAsync(string webhookUrl, DiscordMessage message, CancellationToken ct);

    /// <summary>
    /// Post an Adaptive Card to a Microsoft Teams channel via incoming webhook URL.
    /// </summary>
    Task SendTeamsMessageAsync(string webhookUrl, TeamsMessage message, CancellationToken ct);

    /// <summary>
    /// POST a JSON payload to an arbitrary webhook URL.
    /// </summary>
    Task SendWebhookAsync(string url, object payload, CancellationToken ct);
}
