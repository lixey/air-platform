namespace Platform.Api.Auth.Internals;

internal sealed class UserAuthCache(ICacheProvider cache) : IUserAuthCache
{
  private const string AuthTag = "platform:auth";
  private static readonly string[] SessionTags = [AuthTag];
  private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);

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
      SessionTags,
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

    return await cache.GetOrCreateAsync(
      ProfileKey(userId, tenantId),
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
          user.TimeZone ?? string.Empty,
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
      [AuthTag, UserTag(userId)],
      ct);
  }

  public async Task RemoveSessionAsync(long sessionId, CancellationToken ct = default)
  {
    if (sessionId > 0)
    {
      await cache.RemoveAsync(SessionKey(sessionId), ct);
    }
  }

  public async Task RemoveUserAsync(long userId, CancellationToken ct = default)
  {
    if (userId > 0)
    {
      await cache.RemoveByTagAsync(UserTag(userId), ct);
    }
  }

  public async Task ClearAsync(CancellationToken ct = default) =>
    await cache.RemoveByTagAsync(AuthTag, ct);

  private static string SessionKey(long sessionId) => $"platform:auth:v1:session:{sessionId}";
  private static string UserTag(long userId) => $"platform:auth:u:{userId}";
  private static string ProfileKey(long userId, long? tenantId) =>
    $"platform:auth:v1:profile:{userId}:{tenantId ?? 0}";
}
