using HoldFast.Shared.SessionProcessing;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;

namespace HoldFast.GraphQL.Private.Subscriptions;

/// <summary>
/// Publishes session payload events into HotChocolate's in-memory subscription bus
/// so connected WebSocket clients receive live session updates.
///
/// Registered as a singleton in Program.cs when the private graph is active.
/// In worker-only or public-graph-only modes, NoOpSessionEventPublisher is used instead.
/// </summary>
public sealed class HotChocolateSessionEventPublisher(
    ITopicEventSender sender,
    ILogger<HotChocolateSessionEventPublisher> logger) : ISessionEventPublisher
{
    public async Task PublishSessionPayloadAsync(
        string sessionSecureId,
        DateTime lastUserInteractionTime,
        CancellationToken ct)
    {
        var payload = new SessionPayload
        {
            Events = [],
            Errors = [],
            RageClicks = [],
            SessionComments = [],
            LastUserInteractionTime = lastUserInteractionTime,
        };

        var topic = $"session-payload-{sessionSecureId}";

        try
        {
            await sender.SendAsync(topic, payload, ct);
            logger.LogDebug(
                "Published session payload event for {SecureId}", sessionSecureId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Subscription publish failures are non-fatal — the worker should not stop
            logger.LogWarning(ex,
                "Failed to publish subscription event for session {SecureId}", sessionSecureId);
        }
    }
}
