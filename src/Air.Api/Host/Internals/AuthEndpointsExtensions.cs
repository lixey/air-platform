namespace Platform.Api.Host.Internals;

internal static class AuthEndpointsExtensions
{
  private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(30);
  private static readonly PlatformOptions DefaultOptions = new();

  private static readonly Lazy<string> DummyPasswordHash = new(() =>
    new PasswordHasher().HashPassword("dummy-password-for-timing-defense"));

  private static readonly ConcurrentDictionary<long, GraceRefreshEntry> GraceRefreshTokens = new();
  private static readonly ConcurrentDictionary<(long SessionId, string NextRefreshHash), GraceRefreshEntry> PendingGraceRotations = new();

  internal static void ClearGraceTokensForTesting()
  {
    GraceRefreshTokens.Clear();
    PendingGraceRotations.Clear();
  }

  public static void MapAuthEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/auth/get-login-screen", (HttpContext ctx) =>
      PlatformEndpointsExtensions.WithRequestCulture(
        ctx.Request,
        culture => Results.Ok(AuthProfileBuilder.BuildLoginScreenDto(culture))));

    api.MapPost("/auth/get-profile", HandleGetProfileAsync);
    api.MapPost("/auth/login", HandleLoginAsync);
    api.MapPost("/auth/change-password", HandleChangePasswordAsync);
    api.MapPost("/auth/refresh", HandleRefreshAsync);
    api.MapPost("/auth/logout", HandleLogoutAsync);
    api.MapSwitcherEndpoints();
  }

  private static async Task<IResult> HandleGetProfileAsync(
    HttpContext ctx,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (!userContext.IsAuthenticated || userContext.UserId is null)
    {
      return Results.Unauthorized();
    }

    var cultures = ResolveCultures(ctx.RequestServices);
    if (ctx.Items[AuthClaims.SnapshotItem] is UserAuthSnapshot snapshot && snapshot.IsActive)
    {
      var lang = !string.IsNullOrEmpty(snapshot.LanguageCode)
        ? cultures.Resolve(snapshot.LanguageCode).ShortCode
        : cultures.ResolveRequest(ctx.Request).ShortCode;
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

    var activeTenantId = ctx.RequestServices.GetService<ITenantContext>()?.TenantId;
    var securityInfo = await LoadScopedSecurityInfoAsync(authStore, user, activeTenantId, ct);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, ctx.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildUserProfileDtoAsync(user, securityInfo, userCulture, activeTenantId, authStore, ct));
  }

  internal static async Task<IResult> HandleLoginAsync(
    LoginRequest? req,
    HttpContext ctx,
    IAuthStore authStore,
    IPasswordHasher hasher,
    CancellationToken ct)
  {
    if (!AuthCookieHelper.HasClientHeader(ctx.Request))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var login = req?.Login?.Trim() ?? "";
    var rateLimiter = ctx.RequestServices.GetService<Auth.Internals.LoginRateLimiter>();
    var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
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
    if (user == null || !user.IsActive)
    {
      rateLimiter?.RecordFailure(remoteIp, login);
      hasher.VerifyPassword(req.Password, DummyPasswordHash.Value);
      return Results.Unauthorized();
    }

    var now = ResolveUtcNow(ctx.RequestServices);
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
    var securityInfo = await LoadScopedSecurityInfoAsync(authStore, user, userHomeTenantId, ct);
    var tenantId = userHomeTenantId ?? securityInfo.HomeTenantId;

    var options = ResolveOptions(ctx.RequestServices);
    var accessExpiresAt = now + options.AccessTokenLifetime;
    var refreshExpiresAt = now + options.RefreshTokenLifetime;
    var refreshToken = SessionTokenService.CreateRefreshToken();
    var session = authStore.CreateSession(
      user.Id,
      tenantId,
      SessionTokenService.HashRefreshToken(refreshToken),
      refreshExpiresAt,
      ctx.Request.Headers.UserAgent.ToString(),
      remoteIp);
    await authStore.SaveChangesAsync(ct);

    var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, accessExpiresAt, user.TokenVersion);
    AuthCookieHelper.SetAuthCookies(ctx, accessToken, accessExpiresAt, refreshToken, refreshExpiresAt);

    var cultures = ResolveCultures(ctx.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, ctx.Request, authStore, cultures, ct);
    if (user.LanguageId is > 0)
    {
      AuthCookieHelper.AppendCultureCookie(ctx, userCulture.ShortCode, now);
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

  internal static Task<IResult> HandleChangePasswordAsync(
    ChangePasswordRequest? req,
    HttpContext ctx,
    IUserContext userContext,
    IAuthStore authStore,
    IPasswordHasher hasher,
    CancellationToken ct) =>
    PlatformEndpointsExtensions.WithRequestCultureAsync(ctx.Request, async _culture =>
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

      var now = ResolveUtcNow(ctx.RequestServices);
      var options = ResolveOptions(ctx.RequestServices);
      var accessExpiresAt = now + options.AccessTokenLifetime;
      var refreshExpiresAt = now + options.RefreshTokenLifetime;

      var session = userContext.SessionId is { } sid ? await authStore.FindSessionAsync(sid, ct) : null;
      var securityInfo = await LoadScopedSecurityInfoAsync(
        authStore,
        user,
        session?.TenantId ?? (user as IHasTenant)?.TenantId,
        ct);

      var nextRefreshToken = SessionTokenService.CreateRefreshToken();
      var nextRefreshHash = SessionTokenService.HashRefreshToken(nextRefreshToken);

      if (session != null)
      {
        session.RefreshTokenHash = nextRefreshHash;
        session.PreviousRefreshTokenHash = null;
        session.RotatedAt = now;
        session.ExpiresAt = refreshExpiresAt;
        session.LastUsedAt = now;
        await authStore.SaveChangesAsync(ct);
        if (authCache != null)
        {
          await authCache.RemoveSessionAsync(session.Id, ct);
        }
      }
      else
      {
        session = authStore.CreateSession(
          user.Id,
          (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId,
          nextRefreshHash,
          refreshExpiresAt,
          ctx.Request.Headers.UserAgent.ToString(),
          ctx.Connection.RemoteIpAddress?.ToString());
        await authStore.SaveChangesAsync(ct);
      }

      GraceRefreshTokens.TryRemove(session.Id, out _);
      var accessToken = SessionTokenService.CreateAccessToken(session.Id, user.Id, accessExpiresAt, user.TokenVersion);
      AuthCookieHelper.SetAuthCookies(ctx, accessToken, accessExpiresAt, nextRefreshToken, refreshExpiresAt);

      var cultures = ResolveCultures(ctx.RequestServices);
      var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, ctx.Request, authStore, cultures, ct);
      return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
        user,
        securityInfo,
        userCulture,
        session.TenantId,
        options.AccessTokenLifetime,
        authStore,
        ct));
    });

  internal static async Task<IResult> HandleRefreshAsync(
    HttpContext ctx,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (!AuthCookieHelper.HasClientHeader(ctx.Request))
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
      catch (JsonException)
      {
      }
    }

    var now = ResolveUtcNow(ctx.RequestServices);
    var options = ResolveOptions(ctx.RequestServices);
    BaseAppAuthSession? session = null;
    string? providedHash = null;

    var refreshCookieName = AuthClaims.GetRefreshCookieName(ctx.Request.Host.Port);
    var usedLegacyUnsuffixedCookie = false;
    string? rawRefresh = null;
    if (!string.IsNullOrWhiteSpace(req?.RefreshToken))
    {
      rawRefresh = req.RefreshToken;
    }
    else if (ctx.Request.Cookies.TryGetValue(refreshCookieName, out var refreshCookie) && !string.IsNullOrWhiteSpace(refreshCookie))
    {
      rawRefresh = refreshCookie;
    }
    else if (refreshCookieName != AuthClaims.RefreshCookie &&
             ctx.Request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var legacyRefresh) &&
             !string.IsNullOrWhiteSpace(legacyRefresh))
    {
      rawRefresh = legacyRefresh;
      usedLegacyUnsuffixedCookie = true;
    }

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
    var cookiesIssued = false;

    if (providedHash != null &&
        session.RefreshTokenHash != providedHash &&
        session.PreviousRefreshTokenHash == providedHash)
    {
      if (session.RotatedAt is not { } rotatedAt || now - rotatedAt > GraceWindow)
      {
        GraceRefreshTokens.TryRemove(session.Id, out _);
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

      if (GraceRefreshTokens.TryGetValue(session.Id, out var grace) &&
          grace.PrevHash == providedHash &&
          grace.GraceExpiresAt > now)
      {
        AuthCookieHelper.SetAuthCookies(
          ctx,
          grace.AccessToken,
          grace.AccessExpiresAt,
          grace.RefreshToken,
          grace.RefreshExpiresAt);
        cookiesIssued = true;
      }
    }
    else
    {
      var prevHash = session.RefreshTokenHash;
      var nextRefresh = SessionTokenService.CreateRefreshToken();
      var nextRefreshHash = SessionTokenService.HashRefreshToken(nextRefresh);
      var refreshExpiresAt = now + options.RefreshTokenLifetime;
      var accessExpiresAt = now + options.AccessTokenLifetime;
      var nextAccess = SessionTokenService.CreateAccessToken(session.Id, user.Id, accessExpiresAt, user.TokenVersion);

      session.PreviousRefreshTokenHash = prevHash;
      session.RefreshTokenHash = nextRefreshHash;
      session.RotatedAt = now;
      session.LastUsedAt = now;
      session.ExpiresAt = refreshExpiresAt;

      var hadPriorGrace = GraceRefreshTokens.TryGetValue(session.Id, out var priorGrace);
      var nextGrace = new GraceRefreshEntry(
        prevHash,
        nextAccess,
        accessExpiresAt,
        nextRefresh,
        refreshExpiresAt,
        now + GraceWindow);
      CleanupExpiredGraceTokens(now);
      PendingGraceRotations[(session.Id, nextRefreshHash)] = nextGrace;
      GraceRefreshTokens[session.Id] = nextGrace;

      try
      {
        await authStore.SaveChangesAsync(ct);
        GraceRefreshTokens[session.Id] = nextGrace;
        AuthCookieHelper.SetAuthCookies(ctx, nextAccess, accessExpiresAt, nextRefresh, refreshExpiresAt);
        cookiesIssued = true;
      }
      catch (DbUpdateConcurrencyException ex)
      {
        RollbackGraceEntry(session.Id, nextRefreshHash, nextGrace, hadPriorGrace, priorGrace);
        foreach (var entry in ex.Entries)
        {
          await entry.ReloadAsync(ct);
        }

        if (session.RevokedAt != null ||
            session.ExpiresAt <= now ||
            session.PreviousRefreshTokenHash != providedHash ||
            session.RotatedAt is not { } rotatedAt ||
            now - rotatedAt > GraceWindow)
        {
          return Results.Unauthorized();
        }

        if ((PendingGraceRotations.TryGetValue((session.Id, session.RefreshTokenHash), out var racedGrace) ||
             GraceRefreshTokens.TryGetValue(session.Id, out racedGrace)) &&
            racedGrace.PrevHash == providedHash &&
            racedGrace.GraceExpiresAt > now)
        {
          GraceRefreshTokens[session.Id] = racedGrace;
          AuthCookieHelper.SetAuthCookies(
            ctx,
            racedGrace.AccessToken,
            racedGrace.AccessExpiresAt,
            racedGrace.RefreshToken,
            racedGrace.RefreshExpiresAt);
          cookiesIssued = true;
        }
      }
      catch
      {
        RollbackGraceEntry(session.Id, nextRefreshHash, nextGrace, hadPriorGrace, priorGrace);
        throw;
      }
    }

    if (!cookiesIssued)
    {
      AuthCookieHelper.IssueAccessCookie(ctx, session.Id, user.Id, now + options.AccessTokenLifetime, user.TokenVersion);
      if (usedLegacyUnsuffixedCookie)
      {
        AuthCookieHelper.DeleteLegacyUnsuffixedCookies(ctx);
      }
    }

    if (authCache != null)
    {
      await authCache.RemoveSessionAsync(session.Id, ct);
    }

    var securityInfo = await LoadScopedSecurityInfoAsync(
      authStore,
      user,
      session.TenantId ?? (user as IHasTenant)?.TenantId,
      ct);
    var cultures = ResolveCultures(ctx.RequestServices);
    var userCulture = await AuthProfileBuilder.ResolveUserCultureAsync(user, ctx.Request, authStore, cultures, ct);
    return Results.Ok(await AuthProfileBuilder.BuildSessionDtoAsync(
      user,
      securityInfo,
      userCulture,
      session.TenantId,
      options.AccessTokenLifetime,
      authStore,
      ct));
  }

  internal static async Task<IResult> HandleLogoutAsync(
    HttpContext ctx,
    IUserContext userContext,
    IAuthStore authStore,
    CancellationToken ct)
  {
    if (!AuthCookieHelper.HasClientHeader(ctx.Request))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var logoutRefreshCookieName = AuthClaims.GetRefreshCookieName(ctx.Request.Host.Port);
    BaseAppAuthSession? session = null;
    if (userContext.SessionId is { } sid)
    {
      session = await authStore.FindSessionAsync(sid, ct);
    }
    else if (ctx.Request.Cookies.TryGetValue(logoutRefreshCookieName, out var refreshCookie) &&
             !string.IsNullOrWhiteSpace(refreshCookie))
    {
      session = await authStore.FindSessionByHashAsync(SessionTokenService.HashRefreshToken(refreshCookie), ct);
    }
    else if (logoutRefreshCookieName != AuthClaims.RefreshCookie &&
             ctx.Request.Cookies.TryGetValue(AuthClaims.RefreshCookie, out var legacyLogoutRefresh) &&
             !string.IsNullOrWhiteSpace(legacyLogoutRefresh))
    {
      session = await authStore.FindSessionByHashAsync(SessionTokenService.HashRefreshToken(legacyLogoutRefresh), ct);
    }

    if (session != null)
    {
      GraceRefreshTokens.TryRemove(session.Id, out _);
      session.RevokedAt = ResolveUtcNow(ctx.RequestServices);
      session.RevokedReason = "self";
      await authStore.SaveChangesAsync(ct);

      if (ctx.RequestServices.GetService<IUserAuthCache>() is { } authCache)
      {
        await authCache.RemoveSessionAsync(session.Id, ct);
      }
    }

    AuthCookieHelper.DeleteAuthCookies(ctx);
    return Results.Ok(new { });
  }

  internal static ICultureCatalog ResolveCultures(IServiceProvider services) =>
    services.GetService<ICultureCatalog>() ?? CultureCatalog.DefaultInstance;

  internal static async Task<SecurityInfo> LoadScopedSecurityInfoAsync(
    IAuthStore authStore,
    BaseAppUser user,
    long? tenantId,
    CancellationToken ct)
  {
    using (tenantId != null
      ? AsyncLocalTenantContext.Use(tenantId, null, user.Id)
      : AsyncLocalTenantContext.UseSystemScope(user.Id))
    {
      return await authStore.LoadSecurityInfoAsync(user.Id, ct);
    }
  }

  internal static PlatformOptions ResolveOptions(IServiceProvider services) =>
    services.GetService<PlatformOptions>() ?? DefaultOptions;

  internal static DateTimeOffset ResolveUtcNow(IServiceProvider services) =>
    (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();

  private static void RollbackGraceEntry(
    long sessionId,
    string nextRefreshHash,
    GraceRefreshEntry stagedEntry,
    bool hadPriorGrace,
    GraceRefreshEntry? priorGrace)
  {
    PendingGraceRotations.TryRemove((sessionId, nextRefreshHash), out _);
    if (hadPriorGrace && priorGrace != null)
    {
      GraceRefreshTokens.TryUpdate(sessionId, priorGrace, stagedEntry);
    }
    else
    {
      GraceRefreshTokens.TryRemove(KeyValuePair.Create(sessionId, stagedEntry));
    }
  }

  private static void CleanupExpiredGraceTokens(DateTimeOffset now)
  {
    if (GraceRefreshTokens.Count > 256)
    {
      foreach (var (sid, entry) in GraceRefreshTokens)
      {
        if (entry.GraceExpiresAt <= now)
        {
          GraceRefreshTokens.TryRemove(sid, out _);
        }
      }
    }

    if (PendingGraceRotations.Count > 256)
    {
      foreach (var (key, entry) in PendingGraceRotations)
      {
        if (entry.GraceExpiresAt <= now)
        {
          PendingGraceRotations.TryRemove(key, out _);
        }
      }
    }
  }
}
