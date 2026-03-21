using HoldFast.Domain.Entities;

namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// Result of initializing a session.
/// </summary>
public record SessionInitResult(
    Session Session,
    bool IsNew,
    bool IsDuplicate);

/// <summary>
/// Service that handles full session initialization logic,
/// ported from Go's InitializeSessionImpl.
///
/// Flow:
/// 1. Check for existing session (duplicate detection)
/// 2. Parse device details from User-Agent
/// 3. Lookup geolocation from IP
/// 4. Create Session in PostgreSQL
/// 5. Append device properties to Fields
/// 6. Register service if applicable
/// </summary>
public interface ISessionInitializationService
{
    /// <summary>
    /// Initialize a new session with all metadata.
    /// Called from the worker after Kafka message is received.
    /// </summary>
    Task<SessionInitResult> InitializeSessionAsync(
        string sessionSecureId,
        string? sessionKey,
        int projectId,
        string fingerprint,
        string clientId,
        string clientVersion,
        string firstloadVersion,
        string? clientConfig,
        string environment,
        string? appVersion,
        string? serviceName,
        bool enableStrictPrivacy,
        bool enableRecordingNetworkContents,
        string? privacySetting,
        string? userAgent,
        string? acceptLanguage,
        string? ipAddress,
        CancellationToken ct);

    /// <summary>
    /// Identify a session with user info: sets identifier, detects first-time user,
    /// backfills unidentified sessions with same ClientID.
    /// </summary>
    Task<Session> IdentifySessionAsync(
        string sessionSecureId,
        string userIdentifier,
        object? userObject,
        CancellationToken ct);
}
