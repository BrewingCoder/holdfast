namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// Publishes session payload events so GraphQL subscriptions can stream
/// live session data to connected dashboard clients.
///
/// Implemented by HotChocolateSessionEventPublisher in HoldFast.GraphQL.Private
/// (uses HotChocolate's ITopicEventSender). A no-op implementation is used
/// automatically in worker-only or public-graph-only runtime modes.
/// </summary>
public interface ISessionEventPublisher
{
    /// <summary>
    /// Notifies all subscribers watching <paramref name="sessionSecureId"/> that
    /// new session events have been processed.
    /// </summary>
    Task PublishSessionPayloadAsync(
        string sessionSecureId,
        DateTime lastUserInteractionTime,
        CancellationToken ct);
}
