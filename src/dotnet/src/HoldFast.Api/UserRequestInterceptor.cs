using HoldFast.Shared.Auth;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;

namespace HoldFast.Api;

/// <summary>
/// HC request interceptor that copies HttpContext.User (set by AuthMiddleware)
/// into HC's global state so resolver ClaimsPrincipal parameters resolve correctly.
/// Without this, HC resolvers receive an empty/unauthenticated ClaimsPrincipal.
///
/// Intentionally has no constructor dependencies: HC 16 activates request
/// interceptors against its own scoped provider, which does not include
/// <c>ILogger&lt;T&gt;</c>. Injecting one made every /private GraphQL request
/// 500 with "Unable to resolve service for type ILogger&lt;UserRequestInterceptor&gt;".
/// </summary>
public sealed class UserRequestInterceptor : DefaultHttpRequestInterceptor
{
    public override ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        // Forward the authenticated user into the GraphQL operation request.
        // HOL-16: HC 16 introduced OperationRequestBuilder.SetUser(...) which
        // replaces the old WellKnownContextData.UserState global-state pattern.
        // HC's resolver compiler binds ClaimsPrincipal parameters from this slot.
        if (context.User is not null)
            requestBuilder.SetUser(context.User);

        return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
