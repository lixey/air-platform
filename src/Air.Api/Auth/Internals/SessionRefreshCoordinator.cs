using Platform.Api.Host.Internals;

namespace Platform.Api.Auth.Internals;

internal sealed class SessionRefreshCoordinator
{
  private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(30);

  private readonly ConcurrentDictionary<long, GraceRefreshEntry> _graceTokens = new();
  private readonly ConcurrentDictionary<(long SessionId, string NextRefreshHash), GraceRefreshEntry> _pendingRotations = new();

  public void Remove(long sessionId) =>
    _graceTokens.TryRemove(sessionId, out _);

  public async Task<(bool Unauthorized, bool ReusedOutsideGrace, bool CookiesIssued)> RotateOrReplayAsync(
    HttpContext ctx,
    BaseAppAuthSession session,
    BaseAppUser user,
    string providedHash,
    DateTimeOffset now,
    PlatformOptions options,
    IAuthStore authStore,
    IUserAuthCache? authCache,
    CancellationToken ct)
  {
    if (session.RefreshTokenHash != providedHash &&
        session.PreviousRefreshTokenHash == providedHash)
    {
      if (session.RotatedAt is not { } rotatedAt || now - rotatedAt > GraceWindow)
      {
        _graceTokens.TryRemove(session.Id, out _);
        session.RevokedAt = now;
        session.RevokedReason = "reuse";
        await authStore.SaveChangesAsync(ct);
        if (authCache is not null)
        {
          await authCache.RemoveSessionAsync(session.Id, ct);
        }

        return (Unauthorized: true, ReusedOutsideGrace: true, CookiesIssued: false);
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

        if (session.RevokedAt is not null || session.ExpiresAt <= now)
        {
          return (Unauthorized: true, ReusedOutsideGrace: false, CookiesIssued: false);
        }
      }

      if (_graceTokens.TryGetValue(session.Id, out var grace) &&
          grace.PrevHash == providedHash &&
          grace.GraceExpiresAt > now)
      {
        AuthCookieHelper.SetAuthCookies(
          ctx,
          grace.AccessToken,
          grace.AccessExpiresAt,
          grace.RefreshToken,
          grace.RefreshExpiresAt);
        return (Unauthorized: false, ReusedOutsideGrace: false, CookiesIssued: true);
      }

      return (Unauthorized: false, ReusedOutsideGrace: false, CookiesIssued: false);
    }

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

    var hadPriorGrace = _graceTokens.TryGetValue(session.Id, out var priorGrace);
    var nextGrace = new GraceRefreshEntry(
      prevHash,
      nextAccess,
      accessExpiresAt,
      nextRefresh,
      refreshExpiresAt,
      now + GraceWindow);
    CleanupExpired(now);
    _pendingRotations[(session.Id, nextRefreshHash)] = nextGrace;
    _graceTokens[session.Id] = nextGrace;

    try
    {
      await authStore.SaveChangesAsync(ct);
      _graceTokens[session.Id] = nextGrace;
      AuthCookieHelper.SetAuthCookies(ctx, nextAccess, accessExpiresAt, nextRefresh, refreshExpiresAt);
      return (Unauthorized: false, ReusedOutsideGrace: false, CookiesIssued: true);
    }
    catch (DbUpdateConcurrencyException ex)
    {
      RollbackGraceEntry(session.Id, nextRefreshHash, nextGrace, hadPriorGrace, priorGrace);
      foreach (var entry in ex.Entries)
      {
        await entry.ReloadAsync(ct);
      }

      if (session.RevokedAt is not null ||
          session.ExpiresAt <= now ||
          session.PreviousRefreshTokenHash != providedHash ||
          session.RotatedAt is not { } rotatedAt ||
          now - rotatedAt > GraceWindow)
      {
        return (Unauthorized: true, ReusedOutsideGrace: false, CookiesIssued: false);
      }

      if ((_pendingRotations.TryGetValue((session.Id, session.RefreshTokenHash), out var racedGrace) ||
           _graceTokens.TryGetValue(session.Id, out racedGrace)) &&
          racedGrace.PrevHash == providedHash &&
          racedGrace.GraceExpiresAt > now)
      {
        _graceTokens[session.Id] = racedGrace;
        AuthCookieHelper.SetAuthCookies(
          ctx,
          racedGrace.AccessToken,
          racedGrace.AccessExpiresAt,
          racedGrace.RefreshToken,
          racedGrace.RefreshExpiresAt);
        return (Unauthorized: false, ReusedOutsideGrace: false, CookiesIssued: true);
      }

      return (Unauthorized: false, ReusedOutsideGrace: false, CookiesIssued: false);
    }
    catch
    {
      RollbackGraceEntry(session.Id, nextRefreshHash, nextGrace, hadPriorGrace, priorGrace);
      throw;
    }
  }

  private void RollbackGraceEntry(
    long sessionId,
    string nextRefreshHash,
    GraceRefreshEntry stagedEntry,
    bool hadPriorGrace,
    GraceRefreshEntry? priorGrace)
  {
    _pendingRotations.TryRemove((sessionId, nextRefreshHash), out _);
    if (hadPriorGrace && priorGrace is not null)
    {
      _graceTokens.TryUpdate(sessionId, priorGrace, stagedEntry);
    }
    else
    {
      _graceTokens.TryRemove(KeyValuePair.Create(sessionId, stagedEntry));
    }
  }

  private void CleanupExpired(DateTimeOffset now)
  {
    if (_graceTokens.Count > 256)
    {
      foreach (var (sid, entry) in _graceTokens)
      {
        if (entry.GraceExpiresAt <= now)
        {
          _graceTokens.TryRemove(sid, out _);
        }
      }
    }

    if (_pendingRotations.Count > 256)
    {
      foreach (var (key, entry) in _pendingRotations)
      {
        if (entry.GraceExpiresAt <= now)
        {
          _pendingRotations.TryRemove(key, out _);
        }
      }
    }
  }
}
