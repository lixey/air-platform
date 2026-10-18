namespace Air.Data.Tenancy;

/// <summary>
/// Ambient tenant context backed by AsyncLocal, allowing tenant propagation across async flows.
/// </summary>
public class AsyncLocalTenantContext : ITenantContext
{
  private sealed class TenantBox
  {
    public long? TenantId;
    public IReadOnlySet<string>? Rights;
  }

  private static readonly AsyncLocal<TenantBox?> _box = new();

  public static AsyncLocalTenantContext Instance { get; } = new();

  public long? TenantId => _box.Value?.TenantId;
  public IReadOnlySet<string>? Rights => _box.Value?.Rights;

  public static IDisposable Use(long? tenantId, IReadOnlySet<string>? rights = null)
  {
    var previous = _box.Value;
    _box.Value = new TenantBox
    {
      TenantId = tenantId,
      Rights = rights ?? previous?.Rights
    };
    return new Scope(previous);
  }

  private sealed class Scope(TenantBox? previous) : IDisposable
  {
    public void Dispose() => _box.Value = previous;
  }
}
