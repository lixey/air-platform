namespace Platform.Api.Auth;

/// <summary>
/// Default generic implementation of IAuthStore over an application's DbContext.
/// </summary>
public sealed class AuthStore<TUser, TSession, TRole, TGrant, TAssignment, TTenant, TDbContext>(TDbContext db) : IAuthStore
  where TUser : BaseAppUser
  where TSession : BaseAppAuthSession, new()
  where TRole : BaseAppAuthRole
  where TGrant : BaseAppAuthGrant
  where TAssignment : BaseAppAuthAssignment
  where TTenant : BaseAppTenant
  where TDbContext : PlatformDbContext
{
  public async Task<BaseAppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default)
  {
    var lower = login.ToLowerInvariant();
    return await db.Set<TUser>().IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Login.ToLower() == lower, ct);
  }

  public async Task<BaseAppUser?> FindUserByIdAsync(long id, CancellationToken ct = default) =>
    await db.Set<TUser>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, ct);

  public async Task<BaseAppAuthSession?> FindSessionAsync(long id, CancellationToken ct = default) =>
    await db.Set<TSession>().IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id, ct);

  public async Task<BaseAppAuthSession?> FindSessionByHashAsync(string refreshTokenHash, CancellationToken ct = default) =>
    await db.Set<TSession>().IgnoreQueryFilters()
      .FirstOrDefaultAsync(s => s.RefreshTokenHash == refreshTokenHash || s.PreviousRefreshTokenHash == refreshTokenHash, ct);

  public async Task<SecurityInfo> LoadSecurityInfoAsync(long userId, CancellationToken ct = default) =>
    await GrantedRights.LoadAsync<TAssignment, TRole, TGrant>(db, userId, ct);

  public async Task<List<BaseAppTenant>> GetActiveTenantsAsync(CancellationToken ct = default) =>
    (await db.Set<TTenant>().IgnoreQueryFilters()
      .Where(t => t.IsActive)
      .OrderBy(t => t.SortOrder)
      .ThenBy(t => t.Name)
      .ToListAsync(ct))
    .Cast<BaseAppTenant>()
    .ToList();

  public async Task<BaseAppTenant?> FindTenantByIdAsync(long id, CancellationToken ct = default) =>
    await db.Set<TTenant>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct);

  public BaseAppAuthSession CreateSession(long userId, long? tenantId, Guid familyId, string refreshTokenHash, DateTimeOffset expiresAt, string? client, string? address)
  {
    var session = new TSession
    {
      UserId = userId,
      TenantId = tenantId,
      FamilyId = familyId,
      RefreshTokenHash = refreshTokenHash,
      ExpiresAt = expiresAt,
      LastUsedAt = DateTimeOffset.UtcNow,
      Client = client,
      Address = address
    };
    db.Set<TSession>().Add(session);
    return session;
  }

  public async Task RevokeUserSessionsAsync(long userId, long? exceptSessionId, string reason, CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var sessions = await db.Set<TSession>().IgnoreQueryFilters()
      .Where(s => s.UserId == userId && s.RevokedAt == null && (exceptSessionId == null || s.Id != exceptSessionId.Value))
      .ToListAsync(ct);

    foreach (var s in sessions)
    {
      s.RevokedAt = now;
      s.RevokedReason = reason;
    }
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
