using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.AlertEvaluation;

/// <summary>
/// Evaluates error alerts inline after error grouping.
/// Ported from Go's processErrorAlert / evaluateErrorAlert / sendErrorAlert.
///
/// Algorithm:
/// 1. Load all enabled ErrorAlerts for the project
/// 2. For each alert, check: query/regex match, group not ignored/snoozed,
///    error count in ThresholdWindow >= CountThreshold, frequency cooldown
/// 3. Create ErrorAlertEvent record for deduplication
/// 4. Send notifications (webhooks, Discord, Teams)
/// </summary>
public class AlertEvaluationService : IAlertEvaluationService
{
    private readonly HoldFastDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        HoldFastDbContext db,
        IHttpClientFactory httpClientFactory,
        INotificationService notificationService,
        ILogger<AlertEvaluationService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AlertEvaluationResult> EvaluateErrorAlertsAsync(
        int projectId,
        ErrorGroup errorGroup,
        ErrorObject errorObject,
        CancellationToken ct)
    {
        // Load all enabled error alerts for this project
        var alerts = await _db.Set<ErrorAlert>()
            .Where(a => a.ProjectId == projectId && !a.Disabled)
            .ToListAsync(ct);

        if (alerts.Count == 0)
            return new AlertEvaluationResult(0, 0, []);

        var triggeredIds = new List<int>();

        foreach (var alert in alerts)
        {
            try
            {
                var triggered = await EvaluateSingleAlertAsync(alert, errorGroup, errorObject, ct);
                if (triggered)
                    triggeredIds.Add(alert.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating alert {AlertId} for error group {GroupId}",
                    alert.Id, errorGroup.Id);
            }
        }

        return new AlertEvaluationResult(alerts.Count, triggeredIds.Count, triggeredIds);
    }

    /// <summary>
    /// Evaluate a single error alert against the error group.
    /// Returns true if the alert was triggered.
    /// </summary>
    internal async Task<bool> EvaluateSingleAlertAsync(
        ErrorAlert alert,
        ErrorGroup errorGroup,
        ErrorObject errorObject,
        CancellationToken ct)
    {
        // Check: group not ignored or snoozed
        if (errorGroup.State == ErrorGroupState.Ignored)
            return false;

        // Check: query match (if alert has a query, it must match the error event)
        if (!string.IsNullOrEmpty(alert.Query))
        {
            if (!MatchesQuery(alert.Query, errorGroup.Event))
                return false;
        }

        // Check: regex match (if alert has regex groups, event must match at least one)
        if (!string.IsNullOrEmpty(alert.RegexGroups))
        {
            if (!MatchesRegex(alert.RegexGroups, errorGroup.Event))
                return false;
        }

        // Check: error count in threshold window >= count threshold
        var countThreshold = alert.CountThreshold ?? 1;
        var windowMinutes = alert.ThresholdWindow ?? 30;
        var windowStart = DateTime.UtcNow.AddMinutes(-windowMinutes);

        var errorCount = await _db.ErrorObjects
            .CountAsync(e => e.ErrorGroupId == errorGroup.Id
                && e.Timestamp >= windowStart, ct);

        if (errorCount < countThreshold)
            return false;

        // Check: frequency cooldown — no recent alert event in Frequency seconds
        var frequencySeconds = alert.Frequency ?? 0;
        if (frequencySeconds > 0)
        {
            var cooldownStart = DateTime.UtcNow.AddSeconds(-frequencySeconds);
            var recentAlert = await _db.Set<ErrorAlertEvent>()
                .AnyAsync(e => e.ErrorAlertId == alert.Id
                    && e.ErrorGroupId == errorGroup.Id
                    && e.CreatedAt >= cooldownStart, ct);

            if (recentAlert)
                return false;
        }

        // All checks passed — trigger the alert
        _logger.LogInformation(
            "Alert {AlertId} triggered for error group {GroupId} (count={Count}, threshold={Threshold})",
            alert.Id, errorGroup.Id, errorCount, countThreshold);

        // Create alert event for deduplication
        _db.Set<ErrorAlertEvent>().Add(new ErrorAlertEvent
        {
            ErrorAlertId = alert.Id,
            ErrorGroupId = errorGroup.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        // Pre-load workspace data (must happen on the main thread, before fire-and-forget)
        var project = await _db.Projects
            .Include(p => p.Workspace)
            .FirstOrDefaultAsync(p => p.Id == alert.ProjectId, ct);

        // Send notifications (fire-and-forget, don't block grouping)
        // All DB work is done above; only HTTP calls happen in the background.
        _ = Task.Run(async () =>
        {
            try
            {
                await SendNotificationsAsync(alert, errorGroup, errorObject, project);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notifications for alert {AlertId}", alert.Id);
            }
        }, CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Check if the error event matches the alert's query string.
    /// Simple case-insensitive substring match (matches Go behavior).
    /// </summary>
    internal static bool MatchesQuery(string query, string? errorEvent)
    {
        if (string.IsNullOrEmpty(errorEvent))
            return false;

        return errorEvent.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if the error event matches any of the alert's regex patterns.
    /// RegexGroups is a JSON array of regex strings.
    /// </summary>
    internal static bool MatchesRegex(string regexGroupsJson, string? errorEvent)
    {
        if (string.IsNullOrEmpty(errorEvent))
            return false;

        try
        {
            var patterns = JsonSerializer.Deserialize<List<string>>(regexGroupsJson);
            if (patterns == null) return false;

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                try
                {
                    if (Regex.IsMatch(errorEvent, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                        return true;
                }
                catch (RegexParseException)
                {
                    // Skip invalid regex patterns
                }
                catch (RegexMatchTimeoutException)
                {
                    // Skip patterns that cause catastrophic backtracking
                }
            }
        }
        catch (JsonException)
        {
            // Invalid JSON — treat as no match
        }

        return false;
    }

    /// <summary>
    /// Send notifications via webhook destinations AND workspace-level
    /// Slack, Discord, and Teams integrations.
    /// The project parameter must be pre-loaded (with Workspace included)
    /// before calling this method — no DB access happens here.
    /// </summary>
    private async Task SendNotificationsAsync(
        ErrorAlert alert,
        ErrorGroup errorGroup,
        ErrorObject errorObject,
        Project? project)
    {
        var tasks = new List<Task>();

        // Build a generic AlertNotification for platform-specific builders
        var notification = new AlertNotification
        {
            AlertType = "error",
            Title = alert.Name ?? "Error Alert",
            Description = Truncate(errorGroup.Event, 500),
            ProjectName = project?.Name,
            Severity = "critical",
            Count = null,
            Timestamp = errorObject.Timestamp,
            Metadata = new Dictionary<string, string>
            {
                ["Service"] = errorObject.ServiceName ?? "N/A",
                ["Environment"] = errorObject.Environment ?? "N/A",
                ["Type"] = errorGroup.Type ?? "N/A",
            },
        };

        if (project != null)
        {
            var workspace = project.Workspace;

            if (workspace != null)
            {
                // Slack: workspace-level integration via OAuth token
                if (!string.IsNullOrEmpty(workspace.SlackAccessToken) &&
                    !string.IsNullOrEmpty(alert.ChannelsToNotify))
                {
                    var slackMessage = AlertNotificationBuilder.BuildSlackMessage(notification);
                    var channelIds = ParseChannelIds(alert.ChannelsToNotify);
                    foreach (var channelId in channelIds)
                    {
                        tasks.Add(_notificationService.SendSlackMessageAsync(
                            workspace.SlackAccessToken, channelId, slackMessage, CancellationToken.None));
                    }
                }

                // Discord and Teams via webhook destinations are handled below
            }
        }

        // Webhook destinations (generic, Discord, Teams)
        if (!string.IsNullOrEmpty(alert.WebhookDestinations))
        {
            List<WebhookDestination>? destinations;
            try
            {
                destinations = JsonSerializer.Deserialize<List<WebhookDestination>>(
                    alert.WebhookDestinations,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                destinations = null;
            }

            if (destinations != null)
            {
                foreach (var dest in destinations)
                {
                    if (string.IsNullOrEmpty(dest.Url)) continue;

                    switch (dest.Type?.ToLowerInvariant())
                    {
                        case "discord":
                            var discordMsg = AlertNotificationBuilder.BuildDiscordMessage(notification);
                            tasks.Add(_notificationService.SendDiscordMessageAsync(dest.Url, discordMsg, CancellationToken.None));
                            break;
                        case "microsoft_teams" or "teams":
                            var teamsMsg = AlertNotificationBuilder.BuildTeamsMessage(notification);
                            tasks.Add(_notificationService.SendTeamsMessageAsync(dest.Url, teamsMsg, CancellationToken.None));
                            break;
                        default:
                            var genericPayload = BuildGenericPayload(alert, errorGroup, errorObject);
                            tasks.Add(_notificationService.SendWebhookAsync(dest.Url, genericPayload, CancellationToken.None));
                            break;
                    }
                }
            }
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Parse a JSON array of channel IDs from ChannelsToNotify.
    /// </summary>
    internal static List<string> ParseChannelIds(string? channelsJson)
    {
        if (string.IsNullOrEmpty(channelsJson))
            return [];

        try
        {
            var channels = JsonSerializer.Deserialize<List<ChannelRef>>(
                channelsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return channels?
                .Where(c => !string.IsNullOrEmpty(c.WebhookChannelId))
                .Select(c => c.WebhookChannelId!)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal record ChannelRef(string? WebhookChannel, string? WebhookChannelId);

    private static object BuildGenericPayload(ErrorAlert alert, ErrorGroup errorGroup, ErrorObject errorObject) =>
        new
        {
            alert_name = alert.Name,
            alert_id = alert.Id,
            error_group_id = errorGroup.Id,
            error_event = errorGroup.Event,
            error_type = errorGroup.Type,
            error_url = errorObject.Url,
            environment = errorObject.Environment,
            service_name = errorObject.ServiceName,
            timestamp = errorObject.Timestamp,
        };

    private static string Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Length <= maxLength ? text : text[..maxLength] + "...";

    internal record WebhookDestination(string? Url, string? Type);
}
