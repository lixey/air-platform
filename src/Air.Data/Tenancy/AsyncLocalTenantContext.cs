namespace Platform.Data.Tenancy;

/// <summary>
/// Ambient tenant context backed by AsyncLocal, allowing tenant and user propagation across async flows.
/// </summary>
public class AsyncLocalTenantContext : ITenantContext
{
  private sealed class TenantBox
  {
    public long? TenantId;
    public bool IsSystemScope;
    public long? UserId;
    public string? CorrelationId;
    public IReadOnlySet<string>? Rights;
  }

  private static readonly AsyncLocal<TenantBox?> _box = new();

  public static AsyncLocalTenantContext Instance { get; } = new();

  public long? TenantId => _box.Value?.TenantId;
  public bool IsSystemScope => _box.Value?.IsSystemScope ?? (_box.Value?.TenantId == null);
  public bool HasExplicitSystemScope => _box.Value?.IsSystemScope == true;
  public long? UserId => _box.Value?.UserId;
  public string? CorrelationId => _box.Value?.CorrelationId;
  public IReadOnlySet<string>? Rights => _box.Value?.Rights;

  public static IDisposable UseSystemScope(long? userId = null, string? correlationId = null)
  {
    var previous = _box.Value;
    _box.Value = new TenantBox
    {
      TenantId = null,
      IsSystemScope = true,
      Rights = previous?.Rights,
      UserId = userId ?? previous?.UserId,
      CorrelationId = correlationId ?? previous?.CorrelationId
    };
    return new Scope(previous);
  }

  public static IDisposable Use(
    long? tenantId,
    IReadOnlySet<string>? rights = null,
    long? userId = null,
    string? correlationId = null)
  {
    var previous = _box.Value;
    _box.Value = new TenantBox
    {
      TenantId = tenantId,
      IsSystemScope = tenantId == null,
      Rights = rights ?? previous?.Rights,
      UserId = userId ?? previous?.UserId,
      CorrelationId = correlationId ?? previous?.CorrelationId
    };
    return new Scope(previous);
  }

  private sealed class Scope(TenantBox? previous) : IDisposable
  {
    public void Dispose() => _box.Value = previous;
  }
}
