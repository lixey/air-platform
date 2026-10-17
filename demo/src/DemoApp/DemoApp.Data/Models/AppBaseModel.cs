namespace DemoApp.Data.Models;

public abstract class AppBaseModel : BaseEntity, IHasTenant
{
  public long TenantId { get; set; }
  public virtual AppTenant Tenant { get; set; } = null!;
}
