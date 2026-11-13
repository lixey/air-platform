namespace Platform.Api.Host.Internals;

internal static class SwitcherEndpointsExtensions
{
  public static void MapSwitcherEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/platform-language/switch", HandleSwitchLanguageAsync).AllowAnonymous();
    api.MapPost("/platform-tenant/switch", HandleSwitchTenantAsync);
  }

  private static async Task<IResult> HandleSwitchLanguageAsync(
    SwitchRequest? req,
    HttpContext ctx,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (!AuthCookieHelper.HasClientHeader(ctx.Request))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var now = AuthEndpointsExtensions.ResolveUtcNow(ctx.RequestServices);
    var cultures = AuthEndpointsExtensions.ResolveCultures(ctx.RequestServices);
    var resolvedCode = await authStore.ResolveLanguageCodeAsync(req?.Id, ct);
    var targetCulture = !string.IsNullOrWhiteSpace(resolvedCode)
      ? cultures.Resolve(resolvedCode)
      : cultures.ResolveById(req?.Id);
    AuthCookieHelper.AppendCultureCookie(ctx, targetCulture.ShortCode, now);

    if (userContext.IsAuthenticated && userContext.UserId is { } uid)
    {
      var user = await authStore.FindUserByIdAsync(uid, ct);
      if (user != null && user.IsActive)
      {
        if (resolvedCode != null && req?.Id is > 0 && user.LanguageId != req.Id)
        {
          user.LanguageId = req.Id;
          await authStore.SaveChangesAsync(ct);
          if (ctx.RequestServices.GetService<IUserAuthCache>() is { } authCache)
          {
            await authCache.RemoveUserAsync(user.Id, ct);
          }
        }

        var activeTenantId = ctx.RequestServices.GetService<ITenantContext>()?.TenantId;
        var securityInfo = await AuthEndpointsExtensions.LoadScopedSecurityInfoAsync(authStore, user, activeTenantId, ct);
        return Results.Ok(await AuthProfileBuilder.BuildUserProfileDtoAsync(user, securityInfo, targetCulture, activeTenantId, authStore, ct));
      }
    }

    return Results.Ok(new UserProfileDto
    {
      Language = targetCulture.ShortCode,
      TimeZone = "UTC",
      Permissions = []
    });
  }

  private static async Task<IResult> HandleSwitchTenantAsync(
    SwitchRequest? req,
    HttpContext ctx,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (!AuthCookieHelper.HasClientHeader(ctx.Request))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (req == null)
    {
      return Results.BadRequest(new { detail = "Tenant id is required." });
    }

    if (!userContext.IsAuthenticated || userContext.UserId is null)
    {
      return Results.Unauthorized();
    }

    var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
    if (user == null || !user.IsActive)
    {
      return Results.Unauthorized();
    }

    var sessionId = AuthCookieHelper.ResolveSessionId(ctx.Request, userContext);
    if (sessionId == null)
    {
      return Results.Unauthorized();
    }

    var now = AuthEndpointsExtensions.ResolveUtcNow(ctx.RequestServices);
    var session = await authStore.FindSessionAsync(sessionId.Value, ct);
    if (session == null || session.UserId != user.Id || session.RevokedAt != null || session.ExpiresAt <= now)
    {
      return Results.Unauthorized();
    }

    var targetTenant = await authStore.FindTenantByIdAsync(req.Id, ct);
    if (targetTenant == null || !targetTenant.IsActive)
    {
      return Results.BadRequest(new { detail = "Select an active tenant." });
    }

    var securityInfo = await AuthEndpointsExtensions.LoadScopedSecurityInfoAsync(authStore, user, req.Id, ct);
    var userHomeTenantId = (user as IHasTenant)?.TenantId;
    if (!user.IsRoot && !securityInfo.CanAccessTenant(req.Id) && userHomeTenantId != req.Id)
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    session.TenantId = req.Id;
    session.LastUsedAt = now;
    await authStore.SaveChangesAsync(ct);

    if (ctx.RequestServices.GetService<IUserAuthCache>() is { } authCache)
    {
      await authCache.RemoveSessionAsync(session.Id, ct);
      await authCache.RemoveUserAsync(user.Id, ct);
    }

    var options = AuthEndpointsExtensions.ResolveOptions(ctx.RequestServices);
    AuthCookieHelper.IssueAccessCookie(ctx, session.Id, user.Id, now + options.AccessTokenLifetime, user.TokenVersion);
    var cultures = AuthEndpointsExtensions.ResolveCultures(ctx.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, ctx.Request, authStore, cultures, ct);
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
