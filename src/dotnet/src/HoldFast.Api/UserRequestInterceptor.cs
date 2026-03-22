using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;

namespace HoldFast.Api;

/// <summary>
/// HC request interceptor that copies HttpContext.User (set by AuthMiddleware)
/// into HC's global state so resolver ClaimsPrincipal parameters resolve correctly.
/// Without this, HC resolvers receive an empty/unauthenticated ClaimsPrincipal.
/// </summary>
public sealed class UserRequestInterceptor : DefaultHttpRequestInterceptor
{
    public override ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        // Forward the authenticated user into HC's global state under the well-known key.
        // HC's resolver compiler binds ClaimsPrincipal parameters from WellKnownContextData.UserState.
        requestBuilder.SetGlobalState(WellKnownContextData.UserState, context.User);

        return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
