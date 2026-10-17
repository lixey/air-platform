using Air.Data.Entities;
using DemoApp.Data.Models;
using DemoOrg.Data.Models;

namespace DemoEcom.Data.Models;

public class EcomDocType : BaseDictionaryEntity
{
}

public class EcomDocStatus : BaseDictionaryEntity
{
  public long DocTypeId { get; set; }
  public string? Color { get; set; }
}

public class EcomDoc : BaseEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public long OrganizationId { get; set; } = 1;
  public OrgOrganization? Organization { get; set; }

  public long DocTypeId { get; set; }
  public EcomDocType? DocType { get; set; }

  public long DocStatusId { get; set; }
  public EcomDocStatus? DocStatus { get; set; }

  public string Number { get; set; } = string.Empty;
  public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

  public long? CustomerId { get; set; }
  public OrgCustomer? Customer { get; set; }

  public long? WarehouseId { get; set; }
  public OrgWarehouse? Warehouse { get; set; }

  public long? ManagerId { get; set; }
  public AppUser? Manager { get; set; }

  public decimal Qty { get; set; }
  public decimal Amount { get; set; }
  public decimal PaidAmount { get; set; }

  public string? DeliveryAddress { get; set; }
  public string? Note { get; set; }

  public List<EcomDocLine> Lines { get; set; } = [];
}

public class EcomDocLine : BaseEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public long DocId { get; set; }
  public EcomDoc? Doc { get; set; }

  public long ProductId { get; set; }
  public OrgProduct? Product { get; set; }

  public string Article { get; set; } = string.Empty;
  public int SortOrder { get; set; }
  public decimal Qty { get; set; }
  public decimal QtyReserved { get; set; }
  public decimal QtyDone { get; set; }
  public decimal Price { get; set; }
  public decimal DiscountPercent { get; set; }
  public decimal Amount { get; set; }
}

public class EcomStockBalance : BaseEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public long OrganizationId { get; set; } = 1;
  public OrgOrganization? Organization { get; set; }

  public long WarehouseId { get; set; }
  public OrgWarehouse? Warehouse { get; set; }

  public long ProductId { get; set; }
  public OrgProduct? Product { get; set; }

  public string Article { get; set; } = string.Empty;
  public decimal Qty { get; set; }
  public decimal Reserved { get; set; }
  public decimal Expected { get; set; }
}
