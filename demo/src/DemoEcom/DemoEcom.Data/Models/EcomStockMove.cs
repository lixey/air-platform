using DemoApp.Data.Models;
using DemoOrg.Data.Models;

namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(ProductId), nameof(Date)), Index(nameof(TenantId), nameof(DocId)), Index(nameof(TenantId), nameof(Date))]
public sealed class EcomStockMove : AppBaseModel
{
  public long DocId { get; set; }
  public EcomDoc Doc { get; set; } = null!;

  public long? DocLineId { get; set; }
  public EcomDocLine? DocLine { get; set; }

  public DateOnly Date { get; set; }

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
