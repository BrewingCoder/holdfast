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
}
