namespace Air.Data.Tenancy;

/// <summary>
/// Tenant query filter extensions.
/// </summary>
public static class TenantFilter
{
  public const string Name = "Tenancy";
  public const string Inherited = "TenantInheritance";

  public static IQueryable<T> IgnoreTenantFilter<T>(this IQueryable<T> query) where T : class
  {
    ArgumentNullException.ThrowIfNull(query);
    return query.IgnoreQueryFilters();
  }
}
