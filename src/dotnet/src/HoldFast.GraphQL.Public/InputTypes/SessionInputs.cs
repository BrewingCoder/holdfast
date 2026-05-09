using System.Text.Json;
using HotChocolate;

namespace HoldFast.GraphQL.Public.InputTypes;

/// <summary>
/// Input for initializeSession — called by SDKs when a new session starts.
/// </summary>
public record InitializeSessionInput(
    string SessionSecureId,
    string? SessionKey,
    string OrganizationVerboseId,
    bool EnableStrictPrivacy,
    bool EnableRecordingNetworkContents,
    string ClientVersion,
    string FirstloadVersion,
    string ClientConfig,
    string Environment,
    string? AppVersion,
    string? ServiceName,
    string Fingerprint,
    string ClientId,
    List<string>? NetworkRecordingDomains,
    bool? DisableSessionRecording,
    string? PrivacySetting);

/// <summary>
/// Input for identifySession — associates a user identity with a session.
/// </summary>
public record IdentifySessionInput(
    string SessionSecureId,
    string UserIdentifier,
    JsonElement? UserObject);

/// <summary>
/// Input for addSessionProperties — adds custom properties to a session.
/// </summary>
public record AddSessionPropertiesInput(
    string SessionSecureId,
    JsonElement? PropertiesObject);

/// <summary>
/// Input for addSessionFeedback — user feedback during a session.
/// </summary>
public record AddSessionFeedbackInput(
    string SessionSecureId,
    string? UserName,
    string? UserEmail,
    string Verbatim,
    DateTime Timestamp);

/// <summary>
/// Response from initializeSession.
/// </summary>
public record InitializeSessionResponse(
    string SecureId,
    int ProjectId);

/// <summary>
/// One rrweb session-replay event. Matches Go schema's ReplayEventInput.
/// `_sid` is a numeric sequence id (the underscore-prefix is from rrweb's
/// own type; preserved on the wire via [GraphQLName]). `data` is opaque
/// rrweb event JSON.
/// </summary>
public record ReplayEventInput(
    int Type,
    double Timestamp,
    [property: GraphQLName("_sid")] double Sid,
    JsonElement Data);

/// <summary>
/// Wrapper for a batch of replay events sent in a single pushPayload call.
/// </summary>
public record ReplayEventsInput(
    List<ReplayEventInput?> Events);
