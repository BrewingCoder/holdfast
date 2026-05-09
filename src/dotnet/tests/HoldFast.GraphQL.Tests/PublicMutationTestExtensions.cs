using System.Text.Json;
using HoldFast.Data;
using HoldFast.GraphQL.Public;
using HoldFast.GraphQL.Public.InputTypes;
using HoldFast.Shared.SessionProcessing;
using Microsoft.AspNetCore.Http;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Test-only convenience overloads. Production PublicMutation methods take flat
/// snake_case args (HOL-13: matches the Go schema the SDKs are generated against).
/// These extensions accept the original input records so tests stay compact.
/// </summary>
internal static class PublicMutationTestExtensions
{
    public static Task<InitializeSessionResponse> InitializeSession(
        this PublicMutation mutation,
        InitializeSessionInput input,
        ISessionInitializationService initService,
        IHttpContextAccessor accessor,
        HoldFastDbContext db,
        CancellationToken ct) =>
        mutation.InitializeSession(
            input.SessionSecureId, input.SessionKey, input.OrganizationVerboseId,
            input.EnableStrictPrivacy, input.EnableRecordingNetworkContents,
            input.ClientVersion, input.FirstloadVersion, input.ClientConfig,
            input.Environment, input.AppVersion, input.ServiceName,
            input.Fingerprint, input.ClientId, input.NetworkRecordingDomains,
            input.DisableSessionRecording, input.PrivacySetting,
            initService, accessor, db, ct);

    public static Task<string> IdentifySession(
        this PublicMutation mutation,
        IdentifySessionInput input,
        ISessionInitializationService initService,
        CancellationToken ct) =>
        mutation.IdentifySession(input.SessionSecureId, input.UserIdentifier, input.UserObject,
            initService, ct);

    public static Task<string> AddSessionProperties(
        this PublicMutation mutation,
        AddSessionPropertiesInput input,
        HoldFastDbContext db,
        CancellationToken ct) =>
        mutation.AddSessionProperties(input.SessionSecureId, input.PropertiesObject, db, ct);

    public static Task<string> AddSessionFeedback(
        this PublicMutation mutation,
        AddSessionFeedbackInput input,
        HoldFastDbContext db,
        CancellationToken ct) =>
        mutation.AddSessionFeedback(
            input.SessionSecureId, input.UserName, input.UserEmail,
            input.Verbatim, input.Timestamp, db, ct);

    /// <summary>
    /// Legacy PushPayload signature: payloadId as int, events as raw JSON string.
    /// HOL-14 changed wire types (payloadId → ID/string, events → ReplayEventsInput)
    /// to match the Go schema. Tests historically passed raw strings; this overload
    /// keeps them compact by parsing the legacy events string as a JSON array of
    /// opaque rrweb events.
    /// </summary>
    public static Task<int> PushPayload(
        this PublicMutation mutation,
        string sessionSecureId,
        int? payloadId,
        string eventsJson,
        string messages,
        string resources,
        string? webSocketEvents,
        List<ErrorObjectInput> errors,
        bool? isBeacon,
        bool? hasSessionUnloaded,
        string? highlightLogs,
        IKafkaProducer kafka,
        CancellationToken ct) =>
        mutation.PushPayload(
            sessionSecureId,
            payloadId?.ToString(),
            ParseLegacyEvents(eventsJson),
            messages, resources, webSocketEvents,
            errors.Cast<ErrorObjectInput?>().ToList(),
            isBeacon, hasSessionUnloaded, highlightLogs,
            kafka, ct);

    private static ReplayEventsInput ParseLegacyEvents(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new ReplayEventsInput([]);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var events = new List<ReplayEventInput?>();
                foreach (var elem in doc.RootElement.EnumerateArray())
                    events.Add(new ReplayEventInput(0, 0, 0, elem.Clone()));
                return new ReplayEventsInput(events);
            }
        }
        catch (JsonException) { /* not JSON — wrap as single opaque event */ }

        return new ReplayEventsInput(
            [new ReplayEventInput(0, 0, 0, JsonSerializer.SerializeToElement(raw))]);
    }
}
