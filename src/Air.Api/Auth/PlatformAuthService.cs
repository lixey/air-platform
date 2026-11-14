using Microsoft.AspNetCore.Authorization;
using Platform.Api.Auth.Internals;
using Platform.Api.Host.Internals;

namespace Platform.Api.Auth;

[ServiceRoute("auth")]
public sealed class PlatformAuthService(PlatformAppService? app = null) : PlatformService(app)
{
  private static readonly Lazy<string> DummyPasswordHash = new(static () =>
    new PasswordHasher().HashPassword("dummy-password-for-timing-defense"));

  public override Feature Feature { get; } = new("Platform", "Auth", typeof(PlatformAuthService));

  [AllowAnonymous]
  [Endpoint]
  public LoginScreenDto GetLoginScreen() =>
    AuthProfileBuilder.BuildLoginScreenDto(CultureInfo.CurrentUICulture);

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(UserProfileDto))]
  public async Task<IResult> GetProfile(
    HttpContext httpContext,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct = default)
  {
    if (!userContext.IsAuthenticated || userContext.UserId is null)
    {
      return Results.Unauthorized();
    }

    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    if (httpContext.Items[AuthClaims.SnapshotItem] is UserAuthSnapshot { IsActive: true } snapshot)
    {
      var lang = !string.IsNullOrEmpty(snapshot.LanguageCode)
        ? cultures.Resolve(snapshot.LanguageCode).ShortCode
        : cultures.ResolveRequest(httpContext.Request).ShortCode;
      return Results.Ok(new UserProfileDto
      {
        Id = snapshot.Id,
        Login = snapshot.Login,
        Name = snapshot.Name,
        Email = snapshot.Email,
        Language = lang,
        TimeZone = !string.IsNullOrEmpty(snapshot.TimeZone) ? snapshot.TimeZone : "UTC",
        MustChangePassword = snapshot.MustChangePassword,
        Permissions = [.. snapshot.Permissions]
      });
    }

    var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
    if (user is null or { IsActive: false })
    {
      return Results.Unauthorized();
    }

    var activeTenantId = httpContext.RequestServices.GetService<ITenantContext>()?.TenantId;
    var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(authStore, user, activeTenantId, ct);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, httpContext.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildUserProfileDtoAsync(
      user,
      securityInfo,
      userCulture,
      activeTenantId,
      authStore,
      ct));
  }

  [AllowAnonymous]
  [Endpoint(RequireClientHeader = true, ResponseType = typeof(SessionDto))]
  public async Task<IResult> Login(
    HttpContext httpContext,
    IAuthStore authStore,
    IPasswordHasher hasher,
    LoginRequest? req = null,
    CancellationToken ct = default)
  {
    var login = req?.Login?.Trim() ?? "";
    var rateLimiter = httpContext.RequestServices.GetService<LoginRateLimiter>();
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
    if (rateLimiter?.IsLimited(remoteIp, login) == true)
    {
      return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(req?.Password))
    {
      rateLimiter?.RecordFailure(remoteIp, login);
      return Results.Unauthorized();
    }

    var user = await authStore.FindUserByLoginAsync(login, ct);
    if (user is null or { IsActive: false })
    {
      rateLimiter?.RecordFailure(remoteIp, login);
      hasher.VerifyPassword(req.Password, DummyPasswordHash.Value);
      return Results.Unauthorized();
    }

    var now = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
    if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
    {
      return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (string.IsNullOrEmpty(user.PasswordHash) ||
        hasher.VerifyPassword(req.Password, user.PasswordHash) == PasswordVerification.Failed)
    {
      rateLimiter?.RecordFailure(remoteIp, login);
      var newFailedCount = user.AccessFailedCount + 1;
      var newLockoutCount = user.LockoutCount;
      var newLockoutEnd = user.LockoutEnd;
      if (newFailedCount >= 5)
      {
        newLockoutCount++;
        newFailedCount = 0;
        newLockoutEnd = now.AddMinutes(Math.Min(60, 5 * newLockoutCount));
      }

      await authStore.UpdateLoginStateAsync(user, newFailedCount, newLockoutCount, newLockoutEnd, user.LastLoggedIn, ct);
      return Results.Unauthorized();
    }

    rateLimiter?.Reset(remoteIp, login);
    await authStore.UpdateLoginStateAsync(user, 0, 0, null, now, ct);

    var userHomeTenantId = (user as IHasTenant)?.TenantId;
    var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(authStore, user, userHomeTenantId, ct);
    var tenantId = userHomeTenantId ?? securityInfo.HomeTenantId;

    var options = AuthProfileBuilder.ResolveOptions(httpContext.RequestServices);
    var accessExpiresAt = now + options.AccessTokenLifetime;
    var refreshExpiresAt = now + options.RefreshTokenLifetime;
    var refreshToken = SessionTokenService.CreateRefreshToken();
    var session = authStore.CreateSession(
      user.Id,
      tenantId,
      SessionTokenService.HashRefreshToken(refreshToken),
      refreshExpiresAt,
      httpContext.Request.Headers.UserAgent.ToString(),
      remoteIp);
    await authStore.SaveChangesAsync(ct);

    var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, accessExpiresAt, user.TokenVersion);
    AuthCookieHelper.SetAuthCookies(httpContext, accessToken, accessExpiresAt, refreshToken, refreshExpiresAt);

    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, httpContext.Request, authStore, cultures, ct);
    if (user.LanguageId is > 0)
    {
      AuthCookieHelper.AppendCultureCookie(httpContext, userCulture.ShortCode, now);
    }

    return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
      user,
      securityInfo,
      userCulture,
      tenantId,
      options.AccessTokenLifetime,
      authStore,
      ct));
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(SessionDto))]
  public async Task<IResult> ChangePassword(
    HttpContext httpContext,
    IUserContext userContext,
    IAuthStore authStore,
    IPasswordHasher hasher,
    ChangePasswordRequest? req = null,
    CancellationToken ct = default)
  {
    if (!userContext.IsAuthenticated || userContext.UserId is null)
    {
      return Results.Unauthorized();
    }

    if (req is null || string.IsNullOrWhiteSpace(req.NewPassword))
    {
      return Results.BadRequest(new { detail = L["FormValidationRequired"] });
    }

    if (req.NewPassword.Length < 10)
    {
      return Results.BadRequest(new { detail = L["PasswordTooShort", 10] });
    }

    if (req.NewPassword.Length > 128)
    {
      return Results.BadRequest(new { detail = L["PasswordTooLong", 128] });
    }

    var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
    if (user is null or { IsActive: false })
    {
      return Results.Unauthorized();
    }

    var rateLimiter = httpContext.RequestServices.GetService<LoginRateLimiter>();
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
    var rateKey = $"pwd:{user.Id}";
    if (rateLimiter?.IsLimited(remoteIp, rateKey) == true)
    {
      return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (string.Equals(req.NewPassword, user.Login, StringComparison.OrdinalIgnoreCase))
    {
      return Results.BadRequest(new { detail = L["PasswordIsLogin"] });
    }

    if (string.IsNullOrEmpty(user.PasswordHash) ||
        string.IsNullOrEmpty(req.CurrentPassword) ||
        hasher.VerifyPassword(req.CurrentPassword, user.PasswordHash) == PasswordVerification.Failed)
    {
      rateLimiter?.RecordFailure(remoteIp, rateKey);
      return Results.Json(new { detail = L["InvalidCredentials"] }, statusCode: StatusCodes.Status401Unauthorized);
    }

    rateLimiter?.Reset(remoteIp, rateKey);
    var now = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
    user.AccessFailedCount = 0;
    user.LockoutCount = 0;
    user.LockoutEnd = null;
    user.PasswordHash = hasher.HashPassword(req.NewPassword);
    user.MustChangePassword = false;
    user.TokenVersion++;
    await authStore.RevokeUserSessionsAsync(user.Id, userContext.SessionId, "password-change", ct);
    await authStore.SaveChangesAsync(ct);

    var authCache = httpContext.RequestServices.GetService<IUserAuthCache>();
    if (authCache is not null)
    {
      await authCache.RemoveUserAsync(user.Id, ct);
      if (userContext.SessionId is { } currentSid)
      {
        await authCache.RemoveSessionAsync(currentSid, ct);
      }
    }

    var options = AuthProfileBuilder.ResolveOptions(httpContext.RequestServices);
    var accessExpiresAt = now + options.AccessTokenLifetime;
    var refreshExpiresAt = now + options.RefreshTokenLifetime;
    var existingSession = userContext.SessionId is { } sid ? await authStore.FindSessionAsync(sid, ct) : null;
    var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(
      authStore,
      user,
      existingSession?.TenantId ?? (user as IHasTenant)?.TenantId,
      ct);

    var nextRefreshToken = SessionTokenService.CreateRefreshToken();
    var session = await AuthProfileBuilder.RotateOrCreatePasswordSessionAsync(
      httpContext,
      user,
      securityInfo,
      existingSession,
      SessionTokenService.HashRefreshToken(nextRefreshToken),
      now,
      refreshExpiresAt,
      remoteIp,
      authStore,
      authCache,
      ct);

    httpContext.RequestServices.GetService<SessionRefreshCoordinator>()?.Remove(session.Id);
    var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, accessExpiresAt, user.TokenVersion);
    AuthCookieHelper.SetAuthCookies(httpContext, accessToken, accessExpiresAt, nextRefreshToken, refreshExpiresAt);

    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, httpContext.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
      user,
      securityInfo,
      userCulture,
      session.TenantId,
      options.AccessTokenLifetime,
      authStore,
      ct));
  }

  [AllowAnonymous]
  [Endpoint(RequireClientHeader = true, ResponseType = typeof(SessionDto))]
  public async Task<IResult> Refresh(
    HttpContext httpContext,
    IAuthStore authStore,
    RefreshRequest? req = null,
    CancellationToken ct = default)
  {
    var rateLimiter = httpContext.RequestServices.GetService<LoginRateLimiter>();
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
    if (rateLimiter?.IsRefreshLimited(remoteIp) == true)
    {
      return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var now = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
    var options = AuthProfileBuilder.ResolveOptions(httpContext.RequestServices);
    var (rawRefresh, usedLegacyUnsuffixedCookie) = AuthCookieHelper.ReadRawRefreshToken(httpContext.Request, req);
    if (string.IsNullOrWhiteSpace(rawRefresh))
    {
      return Results.Unauthorized();
    }

    var providedHash = SessionTokenService.HashRefreshToken(rawRefresh);
    var session = await authStore.FindSessionByHashAsync(providedHash, ct);
    if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
    {
      rateLimiter?.RecordRefreshFailure(remoteIp);
      return Results.Unauthorized();
    }

    var user = await authStore.FindUserByIdAsync(session.UserId, ct);
    if (user is null or { IsActive: false })
    {
      rateLimiter?.RecordRefreshFailure(remoteIp);
      return Results.Unauthorized();
    }

    var authCache = httpContext.RequestServices.GetService<IUserAuthCache>();
    var coordinator = httpContext.RequestServices.GetService<SessionRefreshCoordinator>() ?? new SessionRefreshCoordinator();
    var (unauthorized, reusedOutsideGrace, cookiesIssued) = await coordinator.RotateOrReplayAsync(
      httpContext,
      session,
      user,
      providedHash,
      now,
      options,
      authStore,
      authCache,
      ct);

    if (unauthorized)
    {
      if (reusedOutsideGrace)
      {
        rateLimiter?.RecordRefreshFailure(remoteIp);
      }

      return Results.Unauthorized();
    }

    if (!cookiesIssued)
    {
      AuthCookieHelper.IssueAccessCookie(httpContext, session.Id, user.Id, now + options.AccessTokenLifetime, user.TokenVersion);
      if (usedLegacyUnsuffixedCookie)
      {
        AuthCookieHelper.DeleteLegacyUnsuffixedCookies(httpContext);
      }
    }

    if (authCache is not null)
    {
      await authCache.RemoveSessionAsync(session.Id, ct);
    }

    var securityInfo = await AuthProfileBuilder.LoadScopedSecurityInfoAsync(
      authStore,
      user,
      session.TenantId ?? (user as IHasTenant)?.TenantId,
      ct);
    var cultures = AuthProfileBuilder.ResolveCultures(httpContext.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, httpContext.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
      user,
      securityInfo,
      userCulture,
      session.TenantId,
      options.AccessTokenLifetime,
      authStore,
      ct));
  }

  [AllowAnonymous]
  [Endpoint(RequireClientHeader = true)]
  public async Task<IResult> Logout(
    HttpContext httpContext,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct = default)
  {
    var session = await AuthCookieHelper.FindLogoutSessionAsync(httpContext.Request, userContext, authStore, ct);
    if (session is not null)
    {
      httpContext.RequestServices.GetService<SessionRefreshCoordinator>()?.Remove(session.Id);
      session.RevokedAt = AuthProfileBuilder.ResolveUtcNow(httpContext.RequestServices);
      session.RevokedReason = "self";
      await authStore.SaveChangesAsync(ct);

      if (httpContext.RequestServices.GetService<IUserAuthCache>() is { } authCache)
      {
        await authCache.RemoveSessionAsync(session.Id, ct);
      }
    }

    AuthCookieHelper.DeleteAuthCookies(httpContext);
    return Results.Ok(new { });
  }
}
