namespace Platform.Api.Host.Internals;

internal static class AuthCookieHelper
{
  public static bool HasClientHeader(HttpRequest request) =>
    request.Headers.ContainsKey(AuthClaims.ClientHeader);

  public static long? ResolveSessionId(HttpRequest request, IUserContext userContext)
  {
    if (userContext.SessionId is { } sid)
    {
      return sid;
    }

    var accessCookieName = AuthClaims.GetAccessCookieName(request.Host.Port);
    var timeProvider = request.HttpContext.RequestServices.GetService<TimeProvider>();
    if (request.Cookies.TryGetValue(accessCookieName, out var cookieToken) &&
        SessionTokenService.TryValidateAccessToken(cookieToken, out var parsedSid, out _, out _, timeProvider))
    {
      return parsedSid;
    }

    return null;
  }

  public static void SetAuthCookies(
    HttpContext ctx,
    string accessToken,
    DateTimeOffset accessExpiresAt,
    string refreshToken,
    DateTimeOffset refreshExpiresAt)
  {
    IssueAccessCookie(ctx, accessToken, accessExpiresAt);
    IssueRefreshCookie(ctx, refreshToken, refreshExpiresAt);
    if (AuthClaims.GetAccessCookieName(ctx.Request.Host.Port) != AuthClaims.AccessCookie)
    {
      DeleteLegacyUnsuffixedCookies(ctx);
    }
  }

  public static void IssueAccessCookie(
    HttpContext ctx,
    long sessionId,
    long userId,
    DateTimeOffset expiresAt,
    int tokenVersion = 1)
  {
    var accessToken = SessionTokenService.CreateAccessToken(sessionId, userId, expiresAt, tokenVersion);
    IssueAccessCookie(ctx, accessToken, expiresAt);
  }

  public static void IssueAccessCookie(HttpContext ctx, string accessToken, DateTimeOffset expiresAt)
  {
    var accessCookieName = AuthClaims.GetAccessCookieName(ctx.Request.Host.Port);
    ctx.Response.Cookies.Append(accessCookieName, accessToken, new CookieOptions
    {
      Path = "/",
      Expires = expiresAt,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
  }

  public static void IssueRefreshCookie(HttpContext ctx, string refreshToken, DateTimeOffset expiresAt)
  {
    var refreshCookieName = AuthClaims.GetRefreshCookieName(ctx.Request.Host.Port);
    ctx.Response.Cookies.Append(refreshCookieName, refreshToken, new CookieOptions
    {
      Path = "/api/auth",
      Expires = expiresAt,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
  }

  public static void DeleteAuthCookies(HttpContext ctx)
  {
    var accessCookieName = AuthClaims.GetAccessCookieName(ctx.Request.Host.Port);
    var refreshCookieName = AuthClaims.GetRefreshCookieName(ctx.Request.Host.Port);
    ctx.Response.Cookies.Delete(accessCookieName, new CookieOptions
    {
      Path = "/",
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
    ctx.Response.Cookies.Delete(refreshCookieName, new CookieOptions
    {
      Path = "/api/auth",
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
    if (accessCookieName != AuthClaims.AccessCookie)
    {
      DeleteLegacyUnsuffixedCookies(ctx);
    }
  }

  public static void DeleteLegacyUnsuffixedCookies(HttpContext ctx)
  {
    ctx.Response.Cookies.Delete(AuthClaims.AccessCookie, new CookieOptions
    {
      Path = "/",
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
    ctx.Response.Cookies.Delete(AuthClaims.RefreshCookie, new CookieOptions
    {
      Path = "/api/auth",
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = ctx.Request.IsHttps
    });
  }

  public static void AppendCultureCookie(HttpContext ctx, string cultureShortCode, DateTimeOffset now)
  {
    ctx.Response.Cookies.Append("culture", cultureShortCode, new CookieOptions
    {
      Path = "/",
      Expires = now.AddYears(1),
      SameSite = SameSiteMode.Lax
    });
  }

  public static (string? RawRefresh, bool UsedLegacyCookie) ReadRawRefreshToken(HttpRequest request, RefreshRequest? req)
  {
    if (!string.IsNullOrWhiteSpace(req?.RefreshToken))
    {
      return (req.RefreshToken, false);
    }

    var refreshCookieName = AuthClaims.GetRefreshCookieName(request.Host.Port);
    if (request.Cookies.TryGetValue(refreshCookieName, out var refreshCookie) &&
        !string.IsNullOrWhiteSpace(refreshCookie))
    {
      return (refreshCookie, false);
    }

    if (refreshCookieName != AuthClaims.RefreshCookie &&
        request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var legacyRefresh) &&
        !string.IsNullOrWhiteSpace(legacyRefresh))
    {
      return (legacyRefresh, true);
    }

    return (null, false);
  }

  public static async Task<BaseAppAuthSession?> FindLogoutSessionAsync(
    HttpRequest request,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (userContext.SessionId is { } sid)
    {
      return await authStore.FindSessionAsync(sid, ct);
    }

    var refreshCookieName = AuthClaims.GetRefreshCookieName(request.Host.Port);
    if (request.Cookies.TryGetValue(refreshCookieName, out var refreshCookie) &&
        !string.IsNullOrWhiteSpace(refreshCookie))
    {
      return await authStore.FindSessionByHashAsync(SessionTokenService.HashRefreshToken(refreshCookie), ct);
    }

    if (refreshCookieName != AuthClaims.RefreshCookie &&
        request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var legacyLogoutRefresh) &&
        !string.IsNullOrWhiteSpace(legacyLogoutRefresh))
    {
      return await authStore.FindSessionByHashAsync(SessionTokenService.HashRefreshToken(legacyLogoutRefresh), ct);
    }

    return null;
  }
}
