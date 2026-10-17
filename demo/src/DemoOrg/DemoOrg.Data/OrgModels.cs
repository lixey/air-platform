using Air.Data.Entities;

namespace DemoOrg.Data.Models;

public class OrgOrganization : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string? Code { get; set; }
  public string? Color { get; set; } = "sky";
}

public class OrgWarehouse : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string Code { get; set; } = string.Empty;
  public string? Address { get; set; }
  public long? OrganizationId { get; set; }
  public OrgOrganization? Organization { get; set; }
}

public class OrgProduct : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string Article { get; set; } = string.Empty;
  public decimal Price { get; set; }
  public decimal PurchasePrice { get; set; }
  public int StockQuantity { get; set; }
  public long? BrandId { get; set; }
  public OrgBrand? Brand { get; set; }
  public long? CategoryId { get; set; }
  public OrgProductCategory? Category { get; set; }
  public long? UnitId { get; set; }
  public OrgUnit? Unit { get; set; }
}

public class OrgCustomer : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string Code { get; set; } = string.Empty;
  public string? Email { get; set; }
  public string? Phone { get; set; }
  public string? Address { get; set; }
}

public class OrgBrand : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
}

public class OrgProductCategory : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
}

public class OrgCurrency : BaseDictionaryEntity
{
  public string Code { get; set; } = string.Empty;
  public string Symbol { get; set; } = string.Empty;
}

public class OrgUnit : BaseDictionaryEntity
{
  public string Code { get; set; } = string.Empty;
}

public class OrgDeliveryType : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
}

public class OrgPaymentMethod : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
}
