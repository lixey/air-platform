namespace Platform.Api.Tests.Internals;

internal sealed class DisposableScopedAuthStore(DisposableScopedGate gate) : IAuthStore, IDisposable
{
  public bool IsDisposed { get; private set; }

  public void Dispose() => IsDisposed = true;

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

  public Task<BaseAppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<BaseAppUser?>(null);
  }

  public async Task<BaseAppUser?> FindUserByIdAsync(long id, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    gate.EnteredUserLookup.TrySetResult();
    await gate.ReleaseUserLookup.Task.WaitAsync(ct);
    ThrowIfDisposed();
    return new TestAuthUser
    {
      Id = id,
      TenantId = 1,
      Login = "alice",
      Name = "Alice",
      TokenVersion = 3,
      IsActive = true
    };
  }

  public async Task<BaseAppAuthSession?> FindSessionAsync(long id, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    gate.EnteredSessionLookup.TrySetResult();
    await gate.ReleaseSessionLookup.Task.WaitAsync(ct);
    ThrowIfDisposed();
    return new TestAuthSession
    {
      Id = id,
      UserId = 7,
      TenantId = 1,
      RefreshTokenHash = "hash",
      ExpiresAt = TimeProvider.System.GetUtcNow().AddDays(30)
    };
  }

  public Task<BaseAppAuthSession?> FindSessionByHashAsync(string refreshTokenHash, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<BaseAppAuthSession?>(null);
  }

  public Task<SecurityInfo> LoadSecurityInfoAsync(long userId, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.FromResult(new SecurityInfo(["Fake.App.View"], false, 1, new HashSet<long> { 1 }));
  }

  public Task<List<BaseAppTenant>> GetActiveTenantsAsync(CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<List<BaseAppTenant>>([]);
  }

  public Task<BaseAppTenant?> FindTenantByIdAsync(long id, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.FromResult<BaseAppTenant?>(null);
  }

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
    CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.CompletedTask;
  }

  public Task SaveChangesAsync(CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return Task.CompletedTask;
  }
}
