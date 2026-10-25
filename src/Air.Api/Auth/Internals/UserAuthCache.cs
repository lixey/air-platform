namespace Platform.Api.Auth.Internals;

internal sealed class UserAuthCache(ICacheProvider cache) : IUserAuthCache
{
  private const string EpochCacheKey = "platform:auth:v1:epoch";
  private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan EpochTtl = TimeSpan.FromHours(24);

  public async Task<SessionAuthSnapshot?> GetSessionAsync(long sessionId, IAuthStore store, CancellationToken ct = default)
  {
    if (sessionId <= 0)
    {
      return null;
    }

    return await cache.GetOrCreateAsync(
      SessionKey(sessionId),
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
      ct);
  }

  public async Task<UserAuthSnapshot?> GetUserProfileAsync(
    long userId,
    long? tenantId,
    IAuthStore store,
    IServiceProvider services,
    CancellationToken ct = default)
  {
    if (userId <= 0)
    {
      return null;
    }

    var epoch = await GetEpochAsync(ct);
    var userVer = await cache.GetAsync<string>(UserVersionKey(userId), ct) ?? "1";
    var key = ProfileKey(epoch, userVer, userId, tenantId);

    return await cache.GetOrCreateAsync(
      key,
      async token =>
      {
        var user = await store.FindUserByIdAsync(userId, token);
        if (user == null)
        {
          return null;
        }

        var effectiveTenantId = tenantId ?? (user as IHasTenant)?.TenantId;
        SecurityInfo securityInfo;
        Dictionary<string, JsonElement>? customData = null;

        using (effectiveTenantId != null
          ? AsyncLocalTenantContext.Use(effectiveTenantId, null, user.Id)
          : AsyncLocalTenantContext.UseSystemScope(user.Id))
        {
          securityInfo = await store.LoadSecurityInfoAsync(user.Id, token);

          foreach (var provider in services.GetServices<ISessionClaimsProvider>())
          {
            customData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            await provider.PopulateAsync(user.Id, effectiveTenantId ?? securityInfo.HomeTenantId, customData, token);
          }
        }

        return new UserAuthSnapshot(
          user.Id,
          user.Login,
          user.Name ?? user.Login,
          user.Email ?? string.Empty,
          user.TimeZone ?? "UTC",
          user.IsActive,
          user.IsRoot,
          user.TokenVersion,
          user.MustChangePassword,
          (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId,
          securityInfo.Permissions,
          securityInfo.Global,
          securityInfo.AllowedTenantIds?.ToList(),
          customData is { Count: > 0 } ? customData : null);
      },
      ProfileTtl,
      ct);
  }

  public async Task EvictSessionAsync(long sessionId, CancellationToken ct = default)
  {
    if (sessionId > 0)
    {
      await cache.RemoveAsync(SessionKey(sessionId), ct);
    }
  }

  public async Task EvictUserAsync(long userId, CancellationToken ct = default)
  {
    if (userId > 0)
    {
      await cache.SetAsync(UserVersionKey(userId), Guid.NewGuid().ToString("N"), EpochTtl, ct);
    }
  }

  public async Task EvictAllAsync(CancellationToken ct = default) =>
    await cache.SetAsync(EpochCacheKey, Guid.NewGuid().ToString("N"), EpochTtl, ct);

  private async ValueTask<string> GetEpochAsync(CancellationToken ct) =>
    await cache.GetOrCreateAsync(
      EpochCacheKey,
      static _ => ValueTask.FromResult<string?>(Guid.NewGuid().ToString("N")),
      EpochTtl,
      ct) ?? "1";

  private static string SessionKey(long sessionId) => $"platform:auth:v1:session:{sessionId}";
  private static string UserVersionKey(long userId) => $"platform:auth:v1:uver:{userId}";
  private static string ProfileKey(string epoch, string userVer, long userId, long? tenantId) =>
    $"platform:auth:v1:profile:{epoch}:{userVer}:{userId}:{tenantId ?? 0}";
}
