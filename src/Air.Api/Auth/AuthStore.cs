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

  public async Task UpdateLoginStateAsync(
    BaseAppUser user,
    int accessFailedCount,
    int lockoutCount,
    DateTimeOffset? lockoutEnd,
    DateTimeOffset? lastLoggedIn,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(user);
    await db.Set<TUser>().IgnoreQueryFilters()
      .Where(u => u.Id == user.Id)
      .ExecuteUpdateAsync(s => s
        .SetProperty(u => u.AccessFailedCount, accessFailedCount)
        .SetProperty(u => u.LockoutCount, lockoutCount)
        .SetProperty(u => u.LockoutEnd, lockoutEnd)
        .SetProperty(u => u.LastLoggedIn, lastLoggedIn), ct);

    var entry = db.Entry(user);
    if (entry.State != EntityState.Detached)
    {
      SyncProperty(entry.Property(u => u.AccessFailedCount), accessFailedCount);
      SyncProperty(entry.Property(u => u.LockoutCount), lockoutCount);
      SyncProperty(entry.Property(u => u.LockoutEnd), lockoutEnd);
      SyncProperty(entry.Property(u => u.LastLoggedIn), lastLoggedIn);
    }
    else
    {
      user.AccessFailedCount = accessFailedCount;
      user.LockoutCount = lockoutCount;
      user.LockoutEnd = lockoutEnd;
      user.LastLoggedIn = lastLoggedIn;
    }
  }

  private static void SyncProperty<TProp>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<BaseAppUser, TProp> prop, TProp value)
  {
    prop.CurrentValue = value;
    prop.OriginalValue = value;
    prop.IsModified = false;
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

  public async Task<string?> ResolveLanguageCodeAsync(long? languageId, CancellationToken ct = default)
  {
    if (languageId is not > 0 || db.SetOfBase<AppLanguage>() is not { } langSet)
    {
      return null;
    }

    var code = await langSet.IgnoreQueryFilters().AsNoTracking()
      .Where(l => l.Id == languageId.Value && l.IsActive)
      .Select(l => l.Code)
      .FirstOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(code))
    {
      return null;
    }

    var trimmed = code.Trim();
    return trimmed.Length >= 2 ? trimmed[..2].ToLowerInvariant() : trimmed.ToLowerInvariant();
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
