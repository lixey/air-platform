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
  BaseAppAuthSession CreateSession(long userId, long? tenantId, string refreshTokenHash, DateTimeOffset expiresAt, string? client, string? address);
  Task UpdateLoginStateAsync(BaseAppUser user, int accessFailedCount, int lockoutCount, DateTimeOffset? lockoutEnd, DateTimeOffset? lastLoggedIn, CancellationToken ct = default);
  Task RevokeUserSessionsAsync(long userId, long? exceptSessionId, string reason, CancellationToken ct = default) => Task.CompletedTask;
  Task<string?> ResolveLanguageCodeAsync(long? languageId, CancellationToken ct = default) =>
    Task.FromResult<string?>(null);
  Task<string?> ResolveTimeZoneAsync(string? userTimeZone, long? tenantId, CancellationToken ct = default) =>
    Task.FromResult(userTimeZone);
  Task SaveChangesAsync(CancellationToken ct = default);
}
