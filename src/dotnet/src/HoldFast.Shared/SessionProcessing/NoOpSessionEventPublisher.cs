namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// No-op implementation used when subscriptions are not active
/// (e.g., worker-only or public-graph-only runtime modes).
/// </summary>
public sealed class NoOpSessionEventPublisher : ISessionEventPublisher
{
    public Task PublishSessionPayloadAsync(
        string sessionSecureId,
        DateTime lastUserInteractionTime,
        CancellationToken ct) => Task.CompletedTask;
}
