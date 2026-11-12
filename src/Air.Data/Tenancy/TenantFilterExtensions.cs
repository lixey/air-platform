namespace Platform.Data.Tenancy;

/// <summary>
/// Tenant query filter extensions.
/// </summary>
public static class TenantFilterExtensions
{
  public const string Name = "Tenancy";
  public const string OptionalTenant = "OptionalTenant";

  public static IQueryable<T> IgnoreTenantFilter<T>(this IQueryable<T> query) where T : class
  {
    ArgumentNullException.ThrowIfNull(query);
    return query.IgnoreQueryFilters([Name, OptionalTenant]);
  }
}
