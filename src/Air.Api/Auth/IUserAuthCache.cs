namespace Platform.Api.Auth;

/// <summary>
/// Coordinates distributed (Redis) and in-memory caching of user sessions, permissions, and extensible user context data.
/// </summary>
public interface IUserAuthCache
{
  Task<SessionAuthSnapshot?> GetSessionAsync(long sessionId, IAuthStore store, CancellationToken ct = default);

  Task<UserAuthSnapshot?> GetUserProfileAsync(
    long userId,
    long? tenantId,
    IAuthStore store,
    IServiceProvider services,
    CancellationToken ct = default);

  Task EvictSessionAsync(long sessionId, CancellationToken ct = default);

  Task EvictUserAsync(long userId, CancellationToken ct = default);

  Task EvictAllAsync(CancellationToken ct = default);
}
