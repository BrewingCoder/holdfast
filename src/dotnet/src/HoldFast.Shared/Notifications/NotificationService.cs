using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.Notifications;

/// <summary>
/// Sends notifications to Slack, Discord, Teams, and generic webhooks.
/// Uses IHttpClientFactory (named client "AlertWebhooks").
/// All methods log errors but never throw — safe for fire-and-forget usage.
/// Includes one retry with a 1-second delay for transient failures.
/// </summary>
public class NotificationService : INotificationService
{
    private const string HttpClientName = "AlertWebhooks";
    private const string SlackApiUrl = "https://slack.com/api/chat.postMessage";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="NotificationService"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating named HTTP clients.</param>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    public NotificationService(
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendSlackMessageAsync(string accessToken, string channelId, SlackMessage message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(channelId))
        {
            _logger.LogWarning("Slack notification skipped: missing access token or channel ID");
            return;
        }

        message.Channel = channelId;

        await ExecuteWithRetryAsync(async () =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var json = JsonSerializer.Serialize(message);
            using var request = new HttpRequestMessage(HttpMethod.Post, SlackApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }, "Slack", ct);
    }

    /// <inheritdoc />
    public async Task SendDiscordMessageAsync(string webhookUrl, DiscordMessage message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogWarning("Discord notification skipped: missing webhook URL");
            return;
        }

        await ExecuteWithRetryAsync(async () =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync(webhookUrl, message, ct);
            response.EnsureSuccessStatusCode();
        }, "Discord", ct);
    }

    /// <inheritdoc />
    public async Task SendTeamsMessageAsync(string webhookUrl, TeamsMessage message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogWarning("Teams notification skipped: missing webhook URL");
            return;
        }

        await ExecuteWithRetryAsync(async () =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync(webhookUrl, message, ct);
            response.EnsureSuccessStatusCode();
        }, "Teams", ct);
    }

    /// <inheritdoc />
    public async Task SendWebhookAsync(string url, object payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning("Webhook notification skipped: missing URL");
            return;
        }

        await ExecuteWithRetryAsync(async () =>
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();
        }, "Webhook", ct);
    }

    /// <summary>
    /// Execute an async action with one retry on transient failures.
    /// Catches <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/> (timeout).
    /// User-initiated cancellation (via <paramref name="ct"/>) exits immediately without logging an error.
    /// </summary>
    /// <param name="action">The async HTTP action to execute.</param>
    /// <param name="destination">Human-readable destination name for log messages (e.g., "Slack", "Discord").</param>
    /// <param name="ct">Cancellation token. When triggered by the caller, the method exits silently.</param>
    internal async Task ExecuteWithRetryAsync(Func<Task> action, string destination, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await action();
                return; // Success
            }
            catch (HttpRequestException ex) when (attempt == 0)
            {
                _logger.LogWarning(ex, "{Destination} notification attempt {Attempt} failed, retrying", destination, attempt + 1);
                await Task.Delay(RetryDelay, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt == 0)
            {
                // Timeout, not user cancellation
                _logger.LogWarning(ex, "{Destination} notification attempt {Attempt} timed out, retrying", destination, attempt + 1);
                await Task.Delay(RetryDelay, ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "{Destination} notification failed after retry", destination);
                return; // Don't throw
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "{Destination} notification timed out after retry", destination);
                return; // Don't throw
            }
            catch (TaskCanceledException)
            {
                // User-requested cancellation — exit without logging error
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Destination} notification failed with unexpected error", destination);
                return; // Don't throw
            }
        }
    }
}
