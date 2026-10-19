using Air.App.Auth;
using Air.App.Localization;
using Air.App.Metadata;
using Air.Data;
using Air.Data.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Air.App.Host;

internal static class AuthEndpoints
{
  public static void MapAuthEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/auth/get-login-screen", (HttpContext ctx) =>
      PlatformEndpoints.WithRequestCulture(ctx.Request, culture => Results.Ok(new LoginScreenDto
      {
        Title = Localizer.Instance["SignIn"],
        Login = Localizer.Instance["Login"],
        Password = Localizer.Instance["Password"],
        Submit = Localizer.Instance["SignIn"],
        InvalidCredentials = Localizer.Instance["InvalidCredentials"],
        TooManyAttempts = Localizer.Instance["TooManyAttempts"],
        Unavailable = Localizer.Instance["Unavailable"],
        ChangePasswordTitle = Localizer.Instance["ChangePasswordTitle"],
        CurrentPassword = Localizer.Instance["PasswordCurrent"],
        NewPassword = Localizer.Instance["PasswordNew"],
        RepeatPassword = Localizer.Instance["PasswordRepeat"],
        ChangePasswordSubmit = Localizer.Instance["ChangePasswordSubmit"],
        PasswordsDiffer = Localizer.Instance["PasswordsDiffer"],
        SignOut = Localizer.Instance["SignOut"],
        Culture = culture.Name
      })));

    api.MapPost("/auth/get-profile", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (!userContext.IsAuthenticated || userContext.UserId is null)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      return Results.Ok(BuildUserProfileDto(user, securityInfo, IsUkrainian(ctx.Request)));
    });

    api.MapPost("/auth/login", async (LoginRequest? req, HttpContext ctx, IAuthStore authStore, IPasswordHasher hasher, CancellationToken ct) =>
    {
      var login = req?.Login?.Trim() ?? "";
      if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(req?.Password))
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByLoginAsync(login, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var now = DateTimeOffset.UtcNow;
      if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
      {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
      }

      if (string.IsNullOrEmpty(user.PasswordHash) ||
          hasher.VerifyPassword(req.Password, user.PasswordHash) == PasswordVerification.Failed)
      {
        if (!user.IsRoot)
        {
          user.AccessFailedCount++;
          if (user.AccessFailedCount >= 5)
          {
            user.LockoutCount++;
            user.AccessFailedCount = 0;
            user.LockoutEnd = now.AddMinutes(Math.Min(60, 5 * user.LockoutCount));
          }
          await authStore.SaveChangesAsync(ct);
        }
        return Results.Unauthorized();
      }

      user.AccessFailedCount = 0;
      user.LockoutCount = 0;
      user.LockoutEnd = null;
      user.LastLoggedIn = now;

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var tenantId = (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId;
      var refreshToken = SessionTokenService.CreateRefreshToken();
      var session = authStore.CreateSession(
        user.Id,
        tenantId,
        Guid.NewGuid(),
        SessionTokenService.HashRefreshToken(refreshToken),
        now.AddDays(30),
        ctx.Request.Headers.UserAgent.ToString(),
        ctx.Connection.RemoteIpAddress?.ToString()
      );
      await authStore.SaveChangesAsync(ct);

      IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion);
      IssueRefreshCookie(ctx.Response, refreshToken, now.AddDays(30));

      return Results.Ok(BuildSessionDto(user, securityInfo, IsUkrainian(ctx.Request)));
    });

    api.MapPost("/auth/change-password", async (ChangePasswordRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, IPasswordHasher hasher, CancellationToken ct) =>
    {
      if (!userContext.IsAuthenticated || userContext.UserId is null)
      {
        return Results.Unauthorized();
      }

      if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
      {
        return Results.BadRequest(new { error = Localizer.Instance["FormValidationRequired"] });
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      if (string.IsNullOrEmpty(user.PasswordHash) ||
          string.IsNullOrEmpty(req.CurrentPassword) ||
          hasher.VerifyPassword(req.CurrentPassword, user.PasswordHash) == PasswordVerification.Failed)
      {
        return Results.BadRequest(new { error = Localizer.Instance["InvalidCredentials"] });
      }

      user.PasswordHash = hasher.HashPassword(req.NewPassword);
      user.MustChangePassword = false;
      user.TokenVersion++;
      await authStore.RevokeUserSessionsAsync(user.Id, userContext.SessionId, "password-change", ct);
      await authStore.SaveChangesAsync(ct);

      var now = DateTimeOffset.UtcNow;
      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var session = userContext.SessionId is { } sid ? await authStore.FindSessionAsync(sid, ct) : null;
      string? newRefreshToken = null;

      if (session != null)
      {
        session.ExpiresAt = now.AddDays(30);
        session.LastUsedAt = now;
        await authStore.SaveChangesAsync(ct);
      }
      else
      {
        newRefreshToken = SessionTokenService.CreateRefreshToken();
        session = authStore.CreateSession(
          user.Id,
          (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId,
          Guid.NewGuid(),
          SessionTokenService.HashRefreshToken(newRefreshToken),
          now.AddDays(30),
          ctx.Request.Headers.UserAgent.ToString(),
          ctx.Connection.RemoteIpAddress?.ToString()
        );
        await authStore.SaveChangesAsync(ct);
      }

      IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion);
      if (newRefreshToken != null)
      {
        IssueRefreshCookie(ctx.Response, newRefreshToken, now.AddDays(30));
      }

      return Results.Ok(BuildSessionDto(user, securityInfo, IsUkrainian(ctx.Request)));
    });

    api.MapPost("/auth/refresh", async (RefreshRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      var now = DateTimeOffset.UtcNow;
      BaseAppAuthSession? session = null;
      string? providedHash = null;

      var rawRefresh = !string.IsNullOrWhiteSpace(req?.RefreshToken)
        ? req.RefreshToken
        : ctx.Request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var refreshCookie) ? refreshCookie : null;

      if (!string.IsNullOrWhiteSpace(rawRefresh))
      {
        providedHash = SessionTokenService.HashRefreshToken(rawRefresh);
        session = await authStore.FindSessionByHashAsync(providedHash, ct);
      }

      if (session == null)
      {
        var sessionId = ResolveSessionId(ctx.Request, userContext);
        if (sessionId != null)
        {
          session = await authStore.FindSessionAsync(sessionId.Value, ct);
        }
      }

      if (session == null || session.RevokedAt != null || session.ExpiresAt <= now)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(session.UserId, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      if (providedHash != null &&
          session.RefreshTokenHash != providedHash &&
          session.PreviousRefreshTokenHash == providedHash)
      {
        if (session.RotatedAt is not { } rotatedAt || (now - rotatedAt) > TimeSpan.FromSeconds(30))
        {
          session.RevokedAt = now;
          session.RevokedReason = "reuse";
          await authStore.SaveChangesAsync(ct);
          return Results.Unauthorized();
        }

        session.LastUsedAt = now;
        await authStore.SaveChangesAsync(ct);
      }
      else
      {
        var nextRefresh = SessionTokenService.CreateRefreshToken();
        session.PreviousRefreshTokenHash = session.RefreshTokenHash;
        session.RefreshTokenHash = SessionTokenService.HashRefreshToken(nextRefresh);
        session.RotatedAt = now;
        session.LastUsedAt = now;
        await authStore.SaveChangesAsync(ct);
        IssueRefreshCookie(ctx.Response, nextRefresh, session.ExpiresAt);
      }

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion);

      return Results.Ok(BuildSessionDto(user, securityInfo, IsUkrainian(ctx.Request)));
    });

    api.MapPost("/auth/logout", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (userContext.SessionId is { } sid)
      {
        var session = await authStore.FindSessionAsync(sid, ct);
        if (session != null)
        {
          session.RevokedAt = DateTimeOffset.UtcNow;
          session.RevokedReason = "self";
          await authStore.SaveChangesAsync(ct);
        }
      }

      ctx.Response.Cookies.Delete(AuthClaims.AccessCookie, new CookieOptions { Path = "/" });
      ctx.Response.Cookies.Delete(AuthClaims.RefreshCookie, new CookieOptions { Path = "/api/auth" });

      return Results.Ok(new { });
    });

    var switchLanguageHandler = async (SwitchRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      var isUk = req?.Id == 1;
      ctx.Response.Cookies.Append("culture", isUk ? "uk" : "en", new CookieOptions
      {
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddYears(1),
        SameSite = SameSiteMode.Lax
      });

      if (userContext.IsAuthenticated && userContext.UserId is { } uid)
      {
        var user = await authStore.FindUserByIdAsync(uid, ct);
        if (user != null && user.IsActive)
        {
          var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
          return Results.Ok(BuildUserProfileDto(user, securityInfo, isUk));
        }
      }

      return Results.Ok(new UserProfileDto
      {
        Language = isUk ? "uk" : "en",
        TimeZone = "UTC",
        Permissions = []
      });
    };

    api.MapPost("/platform-language/switch", switchLanguageHandler);
    api.MapPost("/PlatformLanguageService/Switch", switchLanguageHandler);

    var switchTenantHandler = async (SwitchRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (req == null)
      {
        return Results.BadRequest(new { error = "Tenant id is required." });
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

      var sessionId = ResolveSessionId(ctx.Request, userContext);
      if (sessionId == null)
      {
        return Results.Unauthorized();
      }

      var session = await authStore.FindSessionAsync(sessionId.Value, ct);
      if (session == null || session.UserId != user.Id || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
      {
        return Results.Unauthorized();
      }

      var targetTenant = await authStore.FindTenantByIdAsync(req.Id, ct);
      if (targetTenant == null || !targetTenant.IsActive)
      {
        return Results.BadRequest(new { error = "Select an active tenant." });
      }

      SecurityInfo securityInfo;
      using (AsyncLocalTenantContext.Use(req.Id, null))
      {
        securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      }

      var userHomeTenantId = (user as IHasTenant)?.TenantId;
      if (!user.IsRoot && !securityInfo.CanAccessTenant(req.Id) && userHomeTenantId != req.Id)
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      session.TenantId = req.Id;
      session.LastUsedAt = DateTimeOffset.UtcNow;
      await authStore.SaveChangesAsync(ct);

      IssueAccessCookie(ctx.Response, session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30), user.TokenVersion);
      return Results.Ok(BuildSessionDto(user, securityInfo, IsUkrainian(ctx.Request)));
    };

    api.MapPost("/platform-tenant/switch", switchTenantHandler);
    api.MapPost("/PlatformTenantService/Switch", switchTenantHandler);
  }

  private static bool IsUkrainian(HttpRequest request) =>
    PlatformEndpoints.ResolveRequestCulture(request).TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

  private static long? ResolveSessionId(HttpRequest request, IUserContext userContext)
  {
    if (userContext.SessionId is { } sid)
    {
      return sid;
    }

    if (request.Cookies.TryGetValue(AuthClaims.AccessCookie, out var cookieToken) &&
        SessionTokenService.TryValidateAccessToken(cookieToken, out var parsedSid, out _, out _))
    {
      return parsedSid;
    }

    return null;
  }

  private static void IssueAccessCookie(HttpResponse response, long sessionId, long userId, DateTimeOffset expires, int tokenVersion = 1)
  {
    var accessToken = SessionTokenService.CreateAccessToken(sessionId, userId, expires, tokenVersion);
    response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
    {
      Path = "/",
      Expires = expires,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true
    });
  }

  private static void IssueRefreshCookie(HttpResponse response, string refreshToken, DateTimeOffset expires)
  {
    response.Cookies.Append(AuthClaims.RefreshCookie, refreshToken, new CookieOptions
    {
      Path = "/api/auth",
      Expires = expires,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true
    });
  }

  private static UserProfileDto BuildUserProfileDto(BaseAppUser user, SecurityInfo securityInfo, bool isUk) =>
    new()
    {
      Id = user.Id,
      Login = user.Login,
      Name = user.Name ?? user.Login,
      Email = user.Email ?? "",
      Language = isUk ? "uk" : "en",
      TimeZone = user.TimeZone ?? "UTC",
      MustChangePassword = user.MustChangePassword,
      Permissions = securityInfo.Permissions.ToList()
    };

  private static SessionDto BuildSessionDto(BaseAppUser user, SecurityInfo securityInfo, bool isUk) =>
    new()
    {
      User = BuildUserProfileDto(user, securityInfo, isUk),
      ExpiresIn = 1800
    };
}
