using HoldFast.Shared.Auth;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HoldFast.Api;

/// <summary>
/// HC request interceptor that copies HttpContext.User (set by AuthMiddleware)
/// into HC's global state so resolver ClaimsPrincipal parameters resolve correctly.
/// Without this, HC resolvers receive an empty/unauthenticated ClaimsPrincipal.
/// </summary>
public sealed class UserRequestInterceptor : DefaultHttpRequestInterceptor
{
    private readonly ILogger<UserRequestInterceptor> _logger;

    public UserRequestInterceptor(ILogger<UserRequestInterceptor> logger)
    {
        _logger = logger;
    }

    public override ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        var uid = context.User?.FindFirst(HoldFastClaimTypes.Uid)?.Value;
        var isAuth = context.User?.Identity?.IsAuthenticated ?? false;
        _logger.LogInformation(
            "UserRequestInterceptor: path={Path} isAuthenticated={IsAuth} uid={Uid}",
            context.Request.Path, isAuth, uid ?? "(null)");

        // Forward the authenticated user into HC's global state under the well-known key.
        // HC's resolver compiler binds ClaimsPrincipal parameters from WellKnownContextData.UserState.
        requestBuilder.SetGlobalState(WellKnownContextData.UserState, context.User);

        return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
