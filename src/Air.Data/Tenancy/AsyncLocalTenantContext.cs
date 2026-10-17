namespace Air.Data.Tenancy;

/// <summary>
/// Ambient tenant context backed by AsyncLocal, allowing tenant propagation across async flows.
/// </summary>
public class AsyncLocalTenantContext : ITenantContext
{
  private static readonly AsyncLocal<long?> _currentTenantId = new();

  public static AsyncLocalTenantContext Instance { get; } = new();

  public long? TenantId => _currentTenantId.Value;

  public static IDisposable Use(long? tenantId)
  {
    var previous = _currentTenantId.Value;
    _currentTenantId.Value = tenantId;
    return new Scope(previous);
  }

  private sealed class Scope(long? previous) : IDisposable
  {
    public void Dispose() => _currentTenantId.Value = previous;
  }
}
