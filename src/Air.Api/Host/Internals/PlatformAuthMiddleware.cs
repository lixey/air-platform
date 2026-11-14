using Platform.Api.Auth.Internals;

namespace Platform.Api.Host.Internals;

internal sealed class PlatformAuthMiddleware(RequestDelegate next)
{
  public async Task InvokeAsync(HttpContext context)
  {
    if (context.RequestServices.GetService<ModuleCatalog>() is { } requestCatalog)
    {
      ServiceEndpointsExtensions.TryBindModuleDbScope(context.Request.Path, requestCatalog, context.RequestServices);
    }

    string? token = null;
    var isCookie = false;

    var accessCookieName = AuthClaims.GetAccessCookieName(context.Request.Host.Port);
    if (context.Request.Cookies.TryGetValue(accessCookieName, out var cookieToken) &&
        !string.IsNullOrWhiteSpace(cookieToken))
    {
      token = cookieToken;
      isCookie = true;
    }
    else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
             authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      token = authHeader.ToString()["Bearer ".Length..].Trim();
    }

    var timeProvider = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
    if (!string.IsNullOrWhiteSpace(token) &&
        SessionTokenService.TryValidateAccessToken(token, out var sessionId, out var userId, out _, out var tokenVersion, timeProvider) &&
        context.RequestServices.GetService<IUserAuthCache>() is { } authCache &&
        HasAuthStoreRegistered(context.RequestServices, authCache, out var fallbackStore))
    {
      var now = timeProvider.GetUtcNow();
      var session = await authCache.GetSessionAsync(sessionId, fallbackStore, context.RequestAborted);
      if (session is not null && session.UserId == userId && session.RevokedAt is null && session.ExpiresAt > now)
      {
        var user = await authCache.GetUserProfileAsync(userId, session.TenantId, fallbackStore, context.RequestAborted);
        if (user is not null && user.IsActive && user.TokenVersion == tokenVersion)
        {
          var claims = new List<Claim>(6)
          {
            new(AuthClaims.Sub, user.Id.ToString()),
            new(AuthClaims.Login, user.Login),
            new(AuthClaims.SessionId, session.Id.ToString()),
            new(AuthClaims.Version, user.TokenVersion.ToString())
          };

          if (user.MustChangePassword)
          {
            claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
          }

          var tenantId = session.TenantId ?? user.HomeTenantId;
          if (tenantId is > 0)
          {
            claims.Add(new Claim(AuthClaims.Tenant, tenantId.Value.ToString()));
          }

          context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "PlatformAuth"));
          context.Items[AuthClaims.SnapshotItem] = user;

          if (isCookie)
          {
            context.Items[AuthClaims.ViaCookieItem] = true;
            if (!HttpMethods.IsGet(context.Request.Method) &&
                !HttpMethods.IsHead(context.Request.Method) &&
                !HttpMethods.IsOptions(context.Request.Method) &&
                !context.Request.Headers.ContainsKey(AuthClaims.ClientHeader))
            {
              context.Response.StatusCode = StatusCodes.Status403Forbidden;
              return;
            }
          }

          if (user.MustChangePassword)
          {
            var reqPath = context.Request.Path.Value ?? "";
            if (reqPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                !reqPath.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
            {
              context.Response.StatusCode = StatusCodes.Status403Forbidden;
              return;
            }
          }

          using (AsyncLocalTenantContext.Use(tenantId, user.PermissionSet, user.Id, context.TraceIdentifier))
          {
            await next(context);
            return;
          }
        }
      }
    }

    using (AsyncLocalTenantContext.Use(null, null, null, context.TraceIdentifier))
    {
      await next(context);
    }
  }

  private static bool HasAuthStoreRegistered(
    IServiceProvider requestServices,
    IUserAuthCache authCache,
    out IAuthStore? fallbackStore)
  {
    if (authCache is UserAuthCache &&
        requestServices.GetService<IServiceProviderIsService>() is { } serviceCheck)
    {
      fallbackStore = null;
      return serviceCheck.IsService(typeof(IAuthStore));
    }

    fallbackStore = requestServices.GetService<IAuthStore>();
    return fallbackStore is not null;
  }
}
