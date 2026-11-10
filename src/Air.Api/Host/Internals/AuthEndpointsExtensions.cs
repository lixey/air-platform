namespace Platform.Api.Host.Internals;

internal static class AuthEndpointsExtensions
{
  private static readonly Lazy<string> DummyPasswordHash = new(() =>
    new PasswordHasher().HashPassword("dummy-password-for-timing-defense"));

  public static void MapAuthEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/auth/get-login-screen", (HttpContext ctx) =>
      PlatformEndpointsExtensions.WithRequestCulture(ctx.Request, culture => Results.Ok(new LoginScreenDto
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

      if (ctx.Items[AuthClaims.SnapshotItem] is UserAuthSnapshot snapshot && snapshot.IsActive)
      {
        var lang = !string.IsNullOrEmpty(snapshot.LanguageCode)
          ? snapshot.LanguageCode
          : IsUkrainian(ctx.Request) ? "uk" : "en";
        return Results.Ok(new UserProfileDto
        {
          Id = snapshot.Id,
          Login = snapshot.Login,
          Name = snapshot.Name,
          Email = snapshot.Email,
          Language = lang,
          TimeZone = !string.IsNullOrEmpty(snapshot.TimeZone) ? snapshot.TimeZone : "UTC",
          MustChangePassword = snapshot.MustChangePassword,
          Permissions = snapshot.Permissions.ToList()
        });
      }

      var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      var isUk = await ResolveIsUkrainianAsync(user, ctx.Request, authStore, ct);
      var activeTenantId = ctx.RequestServices.GetService<ITenantContext>()?.TenantId;
      return Results.Ok(await BuildUserProfileDtoAsync(user, securityInfo, isUk, activeTenantId, authStore, ct));
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
        hasher.VerifyPassword(req.Password, DummyPasswordHash.Value);
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

      await authStore.UpdateLoginStateAsync(user, 0, 0, null, now, ct);

      var userHomeTenantId = (user as IHasTenant)?.TenantId;
      SecurityInfo securityInfo;
      using (AsyncLocalTenantContext.Use(userHomeTenantId, null, user.Id))
      {
        securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      }

      var tenantId = userHomeTenantId ?? securityInfo.HomeTenantId;
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

      IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion, ctx.Request.IsHttps);
      IssueRefreshCookie(ctx.Response, refreshToken, now.AddDays(30), ctx.Request.IsHttps);

      var isUk = await ResolveIsUkrainianAsync(user, ctx.Request, authStore, ct);
      if (user.LanguageId is > 0)
      {
        ctx.Response.Cookies.Append("culture", isUk ? "uk" : "en", new CookieOptions
        {
          Path = "/",
          Expires = now.AddYears(1),
          SameSite = SameSiteMode.Lax
        });
      }

      return Results.Ok(await BuildSessionDtoAsync(user, securityInfo, isUk, tenantId, authStore, ct));
    });

    api.MapPost("/auth/change-password", async (ChangePasswordRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, IPasswordHasher hasher, CancellationToken ct) =>
      await PlatformEndpointsExtensions.WithRequestCultureAsync(ctx.Request, async _ =>
      {
        if (!userContext.IsAuthenticated || userContext.UserId is null)
        {
          return Results.Unauthorized();
        }

        if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
        {
          return Results.BadRequest(new { detail = Localizer.Instance["FormValidationRequired"] });
        }

        if (req.NewPassword.Length < 10)
        {
          return Results.BadRequest(new { detail = Localizer.Instance["PasswordTooShort", 10] });
        }

        if (req.NewPassword.Length > 128)
        {
          return Results.BadRequest(new { detail = Localizer.Instance["PasswordTooLong", 128] });
        }

        var user = await authStore.FindUserByIdAsync(userContext.UserId.Value, ct);
        if (user == null || !user.IsActive)
        {
          return Results.Unauthorized();
        }

        if (string.Equals(req.NewPassword, user.Login, StringComparison.OrdinalIgnoreCase))
        {
          return Results.BadRequest(new { detail = Localizer.Instance["PasswordIsLogin"] });
        }

        if (string.IsNullOrEmpty(user.PasswordHash) ||
            string.IsNullOrEmpty(req.CurrentPassword) ||
            hasher.VerifyPassword(req.CurrentPassword, user.PasswordHash) == PasswordVerification.Failed)
        {
          return Results.Json(new { detail = Localizer.Instance["InvalidCredentials"] }, statusCode: StatusCodes.Status401Unauthorized);
        }

        user.PasswordHash = hasher.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        user.TokenVersion++;
        await authStore.RevokeUserSessionsAsync(user.Id, userContext.SessionId, "password-change", ct);
        await authStore.SaveChangesAsync(ct);

        var authCache = ctx.RequestServices.GetService<IUserAuthCache>();
        if (authCache != null)
        {
          await authCache.RemoveUserAsync(user.Id, ct);
          if (userContext.SessionId is { } currentSid)
          {
            await authCache.RemoveSessionAsync(currentSid, ct);
          }
        }

        var now = DateTimeOffset.UtcNow;
        var session = userContext.SessionId is { } sid ? await authStore.FindSessionAsync(sid, ct) : null;
        SecurityInfo securityInfo;
        using (AsyncLocalTenantContext.Use(session?.TenantId ?? (user as IHasTenant)?.TenantId, null, user.Id))
        {
          securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
        }

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

        IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion, ctx.Request.IsHttps);
        if (newRefreshToken != null)
        {
          IssueRefreshCookie(ctx.Response, newRefreshToken, now.AddDays(30), ctx.Request.IsHttps);
        }

        var isUk = await ResolveIsUkrainianAsync(user, ctx.Request, authStore, ct);
        return Results.Ok(await BuildSessionDtoAsync(user, securityInfo, isUk, session.TenantId, authStore, ct));
      }));

    api.MapPost("/auth/refresh", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (!HasClientHeader(ctx.Request))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      RefreshRequest? req = null;
      if (ctx.Request.ContentLength > 0)
      {
        try
        {
          req = await ctx.Request.ReadFromJsonAsync<RefreshRequest>(cancellationToken: ct);
        }
        catch { }
      }

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

      if (session == null || session.RevokedAt != null || session.ExpiresAt <= now)
      {
        return Results.Unauthorized();
      }

      var user = await authStore.FindUserByIdAsync(session.UserId, ct);
      if (user == null || !user.IsActive)
      {
        return Results.Unauthorized();
      }

      var authCache = ctx.RequestServices.GetService<IUserAuthCache>();
      if (providedHash != null &&
          session.RefreshTokenHash != providedHash &&
          session.PreviousRefreshTokenHash == providedHash)
      {
        if (session.RotatedAt is not { } rotatedAt || now - rotatedAt > TimeSpan.FromSeconds(30))
        {
          session.RevokedAt = now;
          session.RevokedReason = "reuse";
          await authStore.SaveChangesAsync(ct);
          if (authCache != null)
          {
            await authCache.RemoveSessionAsync(session.Id, ct);
          }

          return Results.Unauthorized();
        }

        try
        {
          session.LastUsedAt = now;
          await authStore.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
          foreach (var entry in ex.Entries)
          {
            await entry.ReloadAsync(ct);
          }

          if (session.RevokedAt != null || session.ExpiresAt <= now)
          {
            return Results.Unauthorized();
          }
        }
      }
      else
      {
        var nextRefresh = SessionTokenService.CreateRefreshToken();
        session.PreviousRefreshTokenHash = session.RefreshTokenHash;
        session.RefreshTokenHash = SessionTokenService.HashRefreshToken(nextRefresh);
        session.RotatedAt = now;
        session.LastUsedAt = now;
        try
        {
          await authStore.SaveChangesAsync(ct);
          IssueRefreshCookie(ctx.Response, nextRefresh, session.ExpiresAt, ctx.Request.IsHttps);
        }
        catch (DbUpdateConcurrencyException ex)
        {
          foreach (var entry in ex.Entries)
          {
            await entry.ReloadAsync(ct);
          }

          if (session.RevokedAt != null ||
              session.ExpiresAt <= now ||
              session.PreviousRefreshTokenHash != providedHash ||
              session.RotatedAt is not { } rotatedAt ||
              now - rotatedAt > TimeSpan.FromSeconds(30))
          {
            return Results.Unauthorized();
          }
        }
      }

      if (authCache != null)
      {
        await authCache.RemoveSessionAsync(session.Id, ct);
      }

      SecurityInfo securityInfo;
      using (AsyncLocalTenantContext.Use(session.TenantId ?? (user as IHasTenant)?.TenantId, null, user.Id))
      {
        securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
      }

      IssueAccessCookie(ctx.Response, session.Id, user.Id, now.AddMinutes(30), user.TokenVersion, ctx.Request.IsHttps);

      var isUk = await ResolveIsUkrainianAsync(user, ctx.Request, authStore, ct);
      return Results.Ok(await BuildSessionDtoAsync(user, securityInfo, isUk, session.TenantId, authStore, ct));
    });

    api.MapPost("/auth/logout", async (HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (!HasClientHeader(ctx.Request))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      BaseAppAuthSession? session = null;
      if (userContext.SessionId is { } sid)
      {
        session = await authStore.FindSessionAsync(sid, ct);
      }
      else if (ctx.Request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var refreshCookie) &&
               !string.IsNullOrWhiteSpace(refreshCookie))
      {
        session = await authStore.FindSessionByHashAsync(SessionTokenService.HashRefreshToken(refreshCookie), ct);
      }

      if (session != null)
      {
        session.RevokedAt = DateTimeOffset.UtcNow;
        session.RevokedReason = "self";
        await authStore.SaveChangesAsync(ct);

        var authCache = ctx.RequestServices.GetService<IUserAuthCache>();
        if (authCache != null)
        {
          await authCache.RemoveSessionAsync(session.Id, ct);
        }
      }

      ctx.Response.Cookies.Delete(AuthClaims.AccessCookie, new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = ctx.Request.IsHttps });
      ctx.Response.Cookies.Delete(AuthClaims.RefreshCookie, new CookieOptions { Path = "/api/auth", SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = ctx.Request.IsHttps });

      return Results.Ok(new { });
    });

    var switchLanguageHandler = async (SwitchRequest? req, HttpContext ctx, IUserContext userContext, IAuthStore authStore, CancellationToken ct) =>
    {
      if (!HasClientHeader(ctx.Request))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      var resolvedCode = await authStore.ResolveLanguageCodeAsync(req?.Id, ct);
      var isUk = resolvedCode != null ? resolvedCode == "uk" : req?.Id == 2;
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
          if (resolvedCode != null && req?.Id is > 0 && user.LanguageId != req.Id)
          {
            user.LanguageId = req.Id;
            await authStore.SaveChangesAsync(ct);
            if (ctx.RequestServices.GetService<IUserAuthCache>() is { } authCache)
            {
              await authCache.RemoveUserAsync(user.Id, ct);
            }
          }

          var securityInfo = await authStore.LoadSecurityInfoAsync(user.Id, ct);
          var activeTenantId = ctx.RequestServices.GetService<ITenantContext>()?.TenantId;
          return Results.Ok(await BuildUserProfileDtoAsync(user, securityInfo, isUk, activeTenantId, authStore, ct));
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
      if (!HasClientHeader(ctx.Request))
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
        return Results.BadRequest(new { detail = "Select an active tenant." });
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

      if (ctx.RequestServices.GetService<IUserAuthCache>() is { } authCache)
      {
        await authCache.RemoveSessionAsync(session.Id, ct);
        await authCache.RemoveUserAsync(user.Id, ct);
      }

      IssueAccessCookie(ctx.Response, session.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(30), user.TokenVersion, ctx.Request.IsHttps);
      var isUk = await ResolveIsUkrainianAsync(user, ctx.Request, authStore, ct);
      return Results.Ok(await BuildSessionDtoAsync(user, securityInfo, isUk, req.Id, authStore, ct));
    };

    api.MapPost("/platform-tenant/switch", switchTenantHandler);
    api.MapPost("/PlatformTenantService/Switch", switchTenantHandler);
  }

  private static bool HasClientHeader(HttpRequest request) =>
    request.Headers.ContainsKey(AuthClaims.ClientHeader);

  private static async Task<bool> ResolveIsUkrainianAsync(BaseAppUser user, HttpRequest request, IAuthStore authStore, CancellationToken ct)
  {
    var code = await authStore.ResolveLanguageCodeAsync(user.LanguageId, ct);
    if (!string.IsNullOrEmpty(code))
    {
      return code == "uk";
    }

    return IsUkrainian(request);
  }

  private static bool IsUkrainian(HttpRequest request) =>
    PlatformEndpointsExtensions.ResolveRequestCulture(request).TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

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

  private static void IssueAccessCookie(HttpResponse response, long sessionId, long userId, DateTimeOffset expires, int tokenVersion = 1, bool isSecure = false)
  {
    var accessToken = SessionTokenService.CreateAccessToken(sessionId, userId, expires, tokenVersion);
    response.Cookies.Append(AuthClaims.AccessCookie, accessToken, new CookieOptions
    {
      Path = "/",
      Expires = expires,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = isSecure
    });
  }

  private static void IssueRefreshCookie(HttpResponse response, string refreshToken, DateTimeOffset expires, bool isSecure = false)
  {
    response.Cookies.Append(AuthClaims.RefreshCookie, refreshToken, new CookieOptions
    {
      Path = "/api/auth",
      Expires = expires,
      SameSite = SameSiteMode.Lax,
      HttpOnly = true,
      Secure = isSecure
    });
  }

  private static async Task<UserProfileDto> BuildUserProfileDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    bool isUk,
    long? tenantId,
    IAuthStore authStore,
    CancellationToken ct)
  {
    var effectiveTenantId = tenantId ?? (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId;
    var resolvedTimeZone = await authStore.ResolveTimeZoneAsync(user.TimeZone, effectiveTenantId, ct);
    return new UserProfileDto
    {
      Id = user.Id,
      Login = user.Login,
      Name = user.Name ?? user.Login,
      Email = user.Email ?? "",
      Language = isUk ? "uk" : "en",
      TimeZone = !string.IsNullOrEmpty(resolvedTimeZone) ? resolvedTimeZone : "UTC",
      MustChangePassword = user.MustChangePassword,
      Permissions = securityInfo.Permissions.ToList()
    };
  }

  private static async Task<SessionDto> BuildSessionDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    bool isUk,
    long? tenantId,
    IAuthStore authStore,
    CancellationToken ct) =>
    new()
    {
      User = await BuildUserProfileDtoAsync(user, securityInfo, isUk, tenantId, authStore, ct),
      ExpiresIn = 1800
    };
}
