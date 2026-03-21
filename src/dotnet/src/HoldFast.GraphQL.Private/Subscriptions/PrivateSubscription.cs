using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private.Subscriptions;

/// <summary>
/// GraphQL subscription resolver for the private (dashboard) API.
/// Mirrors Go's private-graph Subscription type in schema.graphqls.
///
/// Uses HotChocolate's in-memory pub/sub. Events are published by
/// HotChocolateSessionEventPublisher when the Kafka worker processes
/// a session events batch.
/// </summary>
[ExtendObjectType(OperationTypeNames.Subscription)]
public class PrivateSubscription
{
    /// <summary>
    /// Streams new session events as they are processed by the worker.
    /// Mirrors Go's session_payload_appended subscription.
    ///
    /// The client connects with a sessionSecureId and an initialEventsCount
    /// (number of events the client already has) so the server can skip
    /// already-delivered events in future implementations.
    /// </summary>
    [Subscribe]
    [Topic("session-payload-{sessionSecureId}")]
    public SessionPayload SessionPayloadAppended(
        string sessionSecureId,
        int initialEventsCount,
        [EventMessage] SessionPayload payload)
        => payload;
}
