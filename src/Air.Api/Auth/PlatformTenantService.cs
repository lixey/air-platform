using Platform.Api.Host.Internals;

namespace Platform.Api.Auth;

[ServiceRoute("platform-tenant")]
public sealed class PlatformTenantService(PlatformAppService? app = null) : PlatformService(app)
{
  public override Feature Feature { get; } = new("Platform", "Tenant", typeof(PlatformTenantService));

  [Endpoint(Auth = EndpointAuth.Custom, RequireClientHeader = true, ResponseType = typeof(SessionDto))]
  public async Task<IResult> Switch(
    SwitchRequest req,
    HttpContext httpContext,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct = default)
  {
    if (req is null or { Id: <= 0 })
    {
      return Results.BadRequest(new { detail = L["FormValidationRequired"] });
    }

    if (!userContext.IsAuthenticated || userContext.UserId is null)
    {
      return Results.Unauthorized();
    }

    var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
    if (user is null or { IsActive: false })
    {
      return Results.Unauthorized();
    }

    var sessionId = AuthCookieHelper.ResolveSessionId(httpContext.Request, userContext);
    if (sessionId is null)
    {
      return Results.Unauthorized();
    }

    var now = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
    var session = await authStore.FindSessionAsync(sessionId.Value, ct);
    if (session is null || session.UserId != user.Id || session.RevokedAt is not null || session.ExpiresAt <= now)
    {
      return Results.Unauthorized();
    }

    var targetTenant = await authStore.FindTenantByIdAsync(req.Id, ct);
    if (targetTenant is null or { IsActive: false })
    {
      return Results.BadRequest(new { detail = L["TenantInactive"] });
    }

    var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(authStore, user, req.Id, ct);
    var userHomeTenantId = (user as IHasTenant)?.TenantId;
    if (!user.IsRoot && !securityInfo.CanAccessTenant(req.Id) && userHomeTenantId != req.Id)
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    session.TenantId = req.Id;
    session.LastUsedAt = now;
    await authStore.SaveChangesAsync(ct);

    if (httpContext.RequestServices.GetService<IUserAuthCache>() is { } authCache)
    {
      await authCache.RemoveSessionAsync(session.Id, ct);
      await authCache.RemoveUserAsync(user.Id, ct);
    }

    var options = AuthProfileBuilder.ResolveOptions(httpContext.RequestServices);
    AuthCookieHelper.IssueAccessCookie(httpContext, session.Id, user.Id, now + options.AccessTokenLifetime, user.TokenVersion);
    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, httpContext.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
      user,
      securityInfo,
      userCulture,
      req.Id,
      options.AccessTokenLifetime,
      authStore,
      ct));
  }
}
