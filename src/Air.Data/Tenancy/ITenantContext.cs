namespace Platform.Data.Tenancy;

/// <summary>
/// Provides access to the current tenant, user, and request context for multi-tenant data scoping and auditing.
/// </summary>
public interface ITenantContext
{
  long? TenantId { get; }
  bool IsSystemScope => TenantId == null;
  long? UserId => null;
  string? CorrelationId => null;
  IReadOnlySet<string>? Rights => null;
}
