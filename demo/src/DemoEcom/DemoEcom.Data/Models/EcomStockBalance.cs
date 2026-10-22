namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(OrganizationId), nameof(WarehouseId), nameof(ProductId), IsUnique = true)]
public sealed class EcomStockBalance : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  public long WarehouseId { get; set; }
  public OrgWarehouse Warehouse { get; set; } = null!;

  public long ProductId { get; set; }
  public OrgProduct Product { get; set; } = null!;

  [Precision(18, 3)]
  public decimal Qty { get; set; }

  [Precision(18, 3)]
  public decimal Reserved { get; set; }

  [Precision(18, 3)]
  public decimal Expected { get; set; }
}
