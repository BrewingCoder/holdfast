namespace HoldFast.GraphQL.Private.Subscriptions;

/// <summary>
/// Payload emitted by the sessionPayloadAppended subscription.
/// Mirrors Go's SessionPayload type in the private graph schema.
///
/// Sent whenever the worker finishes processing a batch of session events
/// from Kafka, signalling dashboard clients that new replay data is available.
/// </summary>
public class SessionPayload
{
    /// <summary>
    /// JSON-serialised RRWeb events that were just processed.
    /// Empty list means the frontend should re-fetch via getSession.
    /// </summary>
    public List<string> Events { get; init; } = [];

    /// <summary>Errors detected in this session batch.</summary>
    public List<SessionPayloadError> Errors { get; init; } = [];

    /// <summary>Rage-click events detected in this session batch.</summary>
    public List<SessionPayloadRageClick> RageClicks { get; init; } = [];

    /// <summary>Comments posted on this session.</summary>
    public List<SessionPayloadComment> SessionComments { get; init; } = [];

    /// <summary>
    /// Timestamp of the last recorded user interaction in this batch.
    /// Used by the live-session viewer to drive the playback cursor.
    /// </summary>
    public DateTime LastUserInteractionTime { get; init; }
}

/// <summary>Minimal error surface for the subscription payload.</summary>
public class SessionPayloadError
{
    public int Id { get; init; }
    public string Event { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Source { get; init; }
    public string? StackTrace { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>Minimal rage-click surface for the subscription payload.</summary>
public class SessionPayloadRageClick
{
    public int Id { get; init; }
    public DateTime StartTimestamp { get; init; }
    public DateTime EndTimestamp { get; init; }
    public int TotalClicks { get; init; }
}

/// <summary>Minimal comment surface for the subscription payload.</summary>
public class SessionPayloadComment
{
    public int Id { get; init; }
    public string? Text { get; init; }
    public DateTime CreatedAt { get; init; }
}
