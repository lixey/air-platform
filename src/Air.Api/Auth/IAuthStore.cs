namespace Platform.Api.Auth;

/// <summary>
/// Provides user, session, and security rights storage operations.
/// </summary>
public interface IAuthStore
{
  Task<BaseAppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default);
  Task<BaseAppUser?> FindUserByIdAsync(long id, CancellationToken ct = default);
  Task<BaseAppAuthSession?> FindSessionAsync(long id, CancellationToken ct = default);
  Task<BaseAppAuthSession?> FindSessionByHashAsync(string refreshTokenHash, CancellationToken ct = default);
  Task<SecurityInfo> LoadSecurityInfoAsync(long userId, CancellationToken ct = default);
  Task<List<BaseAppTenant>> GetActiveTenantsAsync(CancellationToken ct = default);
  Task<BaseAppTenant?> FindTenantByIdAsync(long id, CancellationToken ct = default);
  BaseAppAuthSession CreateSession(long userId, long? tenantId, Guid familyId, string refreshTokenHash, DateTimeOffset expiresAt, string? client, string? address);
  Task RevokeUserSessionsAsync(long userId, long? exceptSessionId, string reason, CancellationToken ct = default) => Task.CompletedTask;
  Task SaveChangesAsync(CancellationToken ct = default);
}
