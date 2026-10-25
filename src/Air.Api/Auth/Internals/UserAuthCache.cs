using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Api.Auth.Internals;

internal sealed class UserAuthCache : IUserAuthCache
{
  private const string EpochCacheKey = "platform:auth:v1:epoch";
  private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);

  private readonly IDistributedCache _cache;
  private readonly bool _isInMemoryOnly;
  private readonly SemaphoreSlim[] _sessionLocks = CreateStripedLocks(64);
  private readonly SemaphoreSlim[] _userLocks = CreateStripedLocks(64);
  private readonly SemaphoreSlim _epochLock = new(1, 1);

  private readonly ConcurrentDictionary<long, (SessionAuthSnapshot? Value, DateTimeOffset ExpiresAt)> _localSessions = new();
  private readonly ConcurrentDictionary<string, (UserAuthSnapshot Value, DateTimeOffset ExpiresAt)> _localProfiles = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<long, string> _localUserVersions = new();
  private volatile string _localEpoch = Guid.NewGuid().ToString("N");

  public UserAuthCache(IDistributedCache cache)
  {
    _cache = cache;
    _isInMemoryOnly = cache is MemoryDistributedCache;
  }

  public async Task<SessionAuthSnapshot?> GetSessionAsync(long sessionId, IAuthStore store, CancellationToken ct = default)
  {
    if (sessionId <= 0)
    {
      return null;
    }

    var now = DateTimeOffset.UtcNow;
    if (_isInMemoryOnly && _localSessions.TryGetValue(sessionId, out var localHit))
    {
      if (localHit.ExpiresAt > now)
      {
        return localHit.Value;
      }

      _localSessions.TryRemove(sessionId, out _);
    }

    var key = SessionKey(sessionId);
    if (!_isInMemoryOnly)
    {
      var (found, cached) = await TryReadDistributedSessionAsync(key, ct);
      if (found)
      {
        return cached;
      }
    }

    var gate = _sessionLocks[(int)(unchecked((ulong)sessionId) & 63UL)];
    await gate.WaitAsync(ct);
    try
    {
      now = DateTimeOffset.UtcNow;
      if (_isInMemoryOnly && _localSessions.TryGetValue(sessionId, out var recheckHit) && recheckHit.ExpiresAt > now)
      {
        return recheckHit.Value;
      }

      if (!_isInMemoryOnly)
      {
        var (found, cached) = await TryReadDistributedSessionAsync(key, ct);
        if (found)
        {
          return cached;
        }
      }

      var dbSession = await store.FindSessionAsync(sessionId, ct);
      var snapshot = dbSession == null
        ? null
        : new SessionAuthSnapshot(
            dbSession.Id,
            dbSession.UserId,
            dbSession.TenantId,
            dbSession.RevokedAt,
            dbSession.ExpiresAt);

      var ttl = snapshot != null && snapshot.ExpiresAt > now && snapshot.ExpiresAt - now < SessionTtl
        ? snapshot.ExpiresAt - now
        : SessionTtl;

      if (_isInMemoryOnly)
      {
        _localSessions[sessionId] = (snapshot, now.Add(ttl));
      }
      else
      {
        await TryWriteDistributedAsync(key, JsonSerializer.Serialize(snapshot, PlatformJsonOptions.DefaultOptions), ttl, ct);
      }

      return snapshot;
    }
    finally
    {
      gate.Release();
    }
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

    var now = DateTimeOffset.UtcNow;
    var epoch = await GetEpochAsync(ct);
    var userVer = await GetUserVersionAsync(userId, ct);
    var key = ProfileKey(epoch, userVer, userId, tenantId);

    if (_isInMemoryOnly && _localProfiles.TryGetValue(key, out var localHit))
    {
      if (localHit.ExpiresAt > now)
      {
        return localHit.Value;
      }

      _localProfiles.TryRemove(key, out _);
    }

    if (!_isInMemoryOnly && await TryReadDistributedProfileAsync(key, ct) is { } redisHit)
    {
      return redisHit;
    }

    var gate = _userLocks[(int)(unchecked((ulong)userId) & 63UL)];
    await gate.WaitAsync(ct);
    try
    {
      now = DateTimeOffset.UtcNow;
      epoch = await GetEpochAsync(ct);
      userVer = await GetUserVersionAsync(userId, ct);
      key = ProfileKey(epoch, userVer, userId, tenantId);

      if (_isInMemoryOnly && _localProfiles.TryGetValue(key, out var recheckHit) && recheckHit.ExpiresAt > now)
      {
        return recheckHit.Value;
      }

      if (!_isInMemoryOnly && await TryReadDistributedProfileAsync(key, ct) is { } redisRecheck)
      {
        return redisRecheck;
      }

      var user = await store.FindUserByIdAsync(userId, ct);
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
        securityInfo = await store.LoadSecurityInfoAsync(user.Id, ct);

        var enrichers = services.GetServices<IUserAuthEnricher>();
        foreach (var enricher in enrichers)
        {
          customData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
          await enricher.EnrichAsync(user.Id, effectiveTenantId ?? securityInfo.HomeTenantId, customData, ct);
        }
      }

      var snapshot = new UserAuthSnapshot(
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

      if (_isInMemoryOnly)
      {
        if (_localProfiles.Count > 2048)
        {
          _localProfiles.Clear();
        }

        _localProfiles[key] = (snapshot, now.Add(ProfileTtl));
      }
      else
      {
        await TryWriteDistributedAsync(key, JsonSerializer.Serialize(snapshot, PlatformJsonOptions.DefaultOptions), ProfileTtl, ct);
      }

      return snapshot;
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task EvictSessionAsync(long sessionId, CancellationToken ct = default)
  {
    if (sessionId <= 0)
    {
      return;
    }

    _localSessions.TryRemove(sessionId, out _);
    if (!_isInMemoryOnly)
    {
      try
      {
        await _cache.RemoveAsync(SessionKey(sessionId), ct);
      }
      catch
      {
      }
    }
  }

  public async Task EvictUserAsync(long userId, CancellationToken ct = default)
  {
    if (userId <= 0)
    {
      return;
    }

    var nextVer = Guid.NewGuid().ToString("N");
    _localUserVersions[userId] = nextVer;

    foreach (var kv in _localSessions)
    {
      if (kv.Value.Value?.UserId == userId)
      {
        _localSessions.TryRemove(kv.Key, out _);
      }
    }

    if (!_isInMemoryOnly)
    {
      await TryWriteDistributedAsync(UserVersionKey(userId), nextVer, ProfileTtl, ct);
    }
  }

  public async Task EvictAllAsync(CancellationToken ct = default)
  {
    var nextEpoch = Guid.NewGuid().ToString("N");
    _localEpoch = nextEpoch;
    _localProfiles.Clear();
    _localSessions.Clear();

    if (!_isInMemoryOnly)
    {
      await TryWriteDistributedAsync(EpochCacheKey, nextEpoch, TimeSpan.FromHours(24), ct);
    }
  }

  private async Task<string> GetEpochAsync(CancellationToken ct)
  {
    if (_isInMemoryOnly)
    {
      return _localEpoch;
    }

    try
    {
      var existing = await _cache.GetStringAsync(EpochCacheKey, ct);
      if (!string.IsNullOrEmpty(existing))
      {
        return existing;
      }
    }
    catch
    {
      return Guid.NewGuid().ToString("N");
    }

    await _epochLock.WaitAsync(ct);
    try
    {
      var recheck = await _cache.GetStringAsync(EpochCacheKey, ct);
      if (!string.IsNullOrEmpty(recheck))
      {
        return recheck;
      }

      var created = Guid.NewGuid().ToString("N");
      await _cache.SetStringAsync(
        EpochCacheKey,
        created,
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) },
        ct);
      return created;
    }
    catch
    {
      return Guid.NewGuid().ToString("N");
    }
    finally
    {
      _epochLock.Release();
    }
  }

  private async Task<string> GetUserVersionAsync(long userId, CancellationToken ct)
  {
    if (_isInMemoryOnly)
    {
      return _localUserVersions.GetOrAdd(userId, static _ => "1");
    }

    try
    {
      var ver = await _cache.GetStringAsync(UserVersionKey(userId), ct);
      return !string.IsNullOrEmpty(ver) ? ver : "1";
    }
    catch
    {
      return Guid.NewGuid().ToString("N");
    }
  }

  private async Task<(bool Found, SessionAuthSnapshot? Value)> TryReadDistributedSessionAsync(string key, CancellationToken ct)
  {
    try
    {
      var raw = await _cache.GetStringAsync(key, ct);
      if (raw == null)
      {
        return (false, null);
      }

      return (true, JsonSerializer.Deserialize<SessionAuthSnapshot>(raw, PlatformJsonOptions.DefaultOptions));
    }
    catch
    {
      return (false, null);
    }
  }

  private async Task<UserAuthSnapshot?> TryReadDistributedProfileAsync(string key, CancellationToken ct)
  {
    try
    {
      var raw = await _cache.GetStringAsync(key, ct);
      return raw == null ? null : JsonSerializer.Deserialize<UserAuthSnapshot>(raw, PlatformJsonOptions.DefaultOptions);
    }
    catch
    {
      return null;
    }
  }

  private async Task TryWriteDistributedAsync(string key, string json, TimeSpan ttl, CancellationToken ct)
  {
    try
    {
      await _cache.SetStringAsync(
        key,
        json,
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
        ct);
    }
    catch
    {
    }
  }

  private static string SessionKey(long sessionId) => $"platform:auth:v1:session:{sessionId}";
  private static string UserVersionKey(long userId) => $"platform:auth:v1:uver:{userId}";
  private static string ProfileKey(string epoch, string userVer, long userId, long? tenantId) =>
    $"platform:auth:v1:profile:{epoch}:{userVer}:{userId}:{tenantId ?? 0}";

  private static SemaphoreSlim[] CreateStripedLocks(int count)
  {
    var locks = new SemaphoreSlim[count];
    for (var i = 0; i < count; i++)
    {
      locks[i] = new SemaphoreSlim(1, 1);
    }

    return locks;
  }
}
