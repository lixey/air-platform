namespace Platform.App.Tests.Internals;

internal sealed class StubMetadataAuthStore : IAuthStore
{
  public Task<BaseAppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default) =>
    Task.FromResult<BaseAppUser?>(null);

  public Task<BaseAppUser?> FindUserByIdAsync(long id, CancellationToken ct = default) =>
    Task.FromResult<BaseAppUser?>(null);

  public Task<BaseAppAuthSession?> FindSessionAsync(long id, CancellationToken ct = default) =>
    Task.FromResult<BaseAppAuthSession?>(null);

  public Task<BaseAppAuthSession?> FindSessionByHashAsync(string refreshTokenHash, CancellationToken ct = default) =>
    Task.FromResult<BaseAppAuthSession?>(null);

  public Task<SecurityInfo> LoadSecurityInfoAsync(long userId, CancellationToken ct = default) =>
    Task.FromResult(new SecurityInfo(["*"], true, 1));

  public Task<List<BaseAppTenant>> GetActiveTenantsAsync(CancellationToken ct = default) =>
    Task.FromResult<List<BaseAppTenant>>([]);

  public Task<BaseAppTenant?> FindTenantByIdAsync(long id, CancellationToken ct = default) =>
    Task.FromResult<BaseAppTenant?>(null);

  public BaseAppAuthSession CreateSession(
    long userId,
    long? tenantId,
    string refreshTokenHash,
    DateTimeOffset expiresAt,
    string? client,
    string? address) =>
    throw new NotSupportedException();

  public Task UpdateLoginStateAsync(
    BaseAppUser user,
    int accessFailedCount,
    int lockoutCount,
    DateTimeOffset? lockoutEnd,
    DateTimeOffset? lastLoggedIn,
    CancellationToken ct = default) =>
    Task.CompletedTask;

  public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
