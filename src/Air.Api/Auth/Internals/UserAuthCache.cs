namespace Platform.Api.Auth.Internals;

internal sealed class UserAuthCache(ICacheProvider cache) : IUserAuthCache
{
  private const string AuthTag = "platform:auth";
  private const int MaxUserGenerations = 10_000;
  private const int MaxSessionGenerations = 50_000;

  private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);

  private readonly ConcurrentDictionary<long, long> _userGenerations = new();
  private readonly ConcurrentDictionary<long, long> _sessionGenerations = new();
  private long _globalGeneration;

  public async Task<SessionAuthSnapshot?> GetSessionAsync(long sessionId, IAuthStore store, CancellationToken ct = default)
  {
    if (sessionId <= 0)
    {
      return null;
    }

    var gGen = Volatile.Read(ref _globalGeneration);
    var sGen = _sessionGenerations.GetValueOrDefault(sessionId);

    return await cache.GetOrCreateAsync(
      SessionKey(gGen, sGen, sessionId),
      async token =>
      {
        var dbSession = await store.FindSessionAsync(sessionId, token);
        return dbSession == null
          ? null
          : new SessionAuthSnapshot(
              dbSession.Id,
              dbSession.UserId,
              dbSession.TenantId,
              dbSession.RevokedAt,
              dbSession.ExpiresAt);
      },
      SessionTtl,
      [AuthTag, SessionTag(sessionId)],
      ct);
  }

  public async Task<UserAuthSnapshot?> GetUserProfileAsync(
    long userId,
    long? tenantId,
    IAuthStore store,
    CancellationToken ct = default)
  {
    if (userId <= 0)
    {
      return null;
    }

    var gGen = Volatile.Read(ref _globalGeneration);
    var uGen = _userGenerations.GetValueOrDefault(userId);

    return await cache.GetOrCreateAsync(
      ProfileKey(gGen, uGen, userId, tenantId),
      async token =>
      {
        var user = await store.FindUserByIdAsync(userId, token);
        if (user == null)
        {
          return null;
        }

        var effectiveTenantId = tenantId ?? (user as IHasTenant)?.TenantId;
        SecurityInfo securityInfo;

        using (effectiveTenantId != null
          ? AsyncLocalTenantContext.Use(effectiveTenantId, null, user.Id)
          : AsyncLocalTenantContext.UseSystemScope(user.Id))
        {
          securityInfo = await store.LoadSecurityInfoAsync(user.Id, token);
        }

        var languageCode = await store.ResolveLanguageCodeAsync(user.LanguageId, token);
        var resolvedTimeZone = await store.ResolveTimeZoneAsync(user.TimeZone, effectiveTenantId ?? securityInfo.HomeTenantId, token);

        return new UserAuthSnapshot(
          user.Id,
          user.Login,
          user.Name ?? user.Login,
          user.Email ?? string.Empty,
          resolvedTimeZone ?? string.Empty,
          user.IsActive,
          user.IsRoot,
          user.TokenVersion,
          user.MustChangePassword,
          (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId,
          securityInfo.Permissions,
          securityInfo.Global,
          securityInfo.AllowedTenantIds?.ToList(),
          languageCode);
      },
      ProfileTtl,
      [AuthTag, UserTag(userId)],
      ct);
  }

  public async Task RemoveSessionAsync(long sessionId, CancellationToken ct = default)
  {
    if (sessionId > 0)
    {
      BumpGeneration(_sessionGenerations, sessionId, MaxSessionGenerations);
      await cache.RemoveByTagAsync(SessionTag(sessionId), ct);
    }
  }

  public async Task RemoveUserAsync(long userId, CancellationToken ct = default)
  {
    if (userId > 0)
    {
      BumpGeneration(_userGenerations, userId, MaxUserGenerations);
      await cache.RemoveByTagAsync(UserTag(userId), ct);
    }
  }

  public async Task ClearAsync(CancellationToken ct = default)
  {
    Interlocked.Increment(ref _globalGeneration);
    _userGenerations.Clear();
    _sessionGenerations.Clear();
    await cache.RemoveByTagAsync(AuthTag, ct);
  }

  private void BumpGeneration(ConcurrentDictionary<long, long> map, long id, int maxEntries)
  {
    if (map.Count >= maxEntries && !map.ContainsKey(id))
    {
      Interlocked.Increment(ref _globalGeneration);
      map.Clear();
      return;
    }

    map.AddOrUpdate(id, 1, static (_, prev) => prev + 1);
  }

  private static string SessionTag(long sessionId) => $"platform:auth:s:{sessionId}";
  private static string UserTag(long userId) => $"platform:auth:u:{userId}";
  private static string SessionKey(long gGen, long sGen, long sessionId) =>
    $"platform:auth:v1:g{gGen}:s{sGen}:session:{sessionId}";
  private static string ProfileKey(long gGen, long uGen, long userId, long? tenantId) =>
    $"platform:auth:v1:g{gGen}:u{uGen}:profile:{userId}:{tenantId ?? 0}";
}
