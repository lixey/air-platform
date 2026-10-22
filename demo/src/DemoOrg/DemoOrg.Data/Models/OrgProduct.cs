namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(SupplierId), nameof(BrandId), nameof(Article), IsUnique = true), Index(nameof(TenantId), nameof(Name))]
public sealed class OrgProduct : AppBaseModel
{
  public long SupplierId { get; set; }
  public OrgCounterparty Supplier { get; set; } = null!;

  public long BrandId { get; set; }
  public OrgBrand Brand { get; set; } = null!;

  [Required, MaxLength(64)]
  public string Article { get; set; } = string.Empty;

  [Required, MaxLength(256)]
  public string Name { get; set; } = string.Empty;

  public long? CategoryId { get; set; }
  public OrgProductCategory? Category { get; set; }

  public long UnitId { get; set; }
  public OrgUnit Unit { get; set; } = null!;

  [MaxLength(14)]
  public string? Barcode { get; set; }

  [MaxLength(10)]
  public string? CustomsCode { get; set; }

  [Precision(18, 2)]
  public decimal Price { get; set; }

  [Precision(18, 2)]
  public decimal PurchasePrice { get; set; }

  [Precision(12, 3)]
  public decimal Weight { get; set; }

  public bool IsActive { get; set; } = true;

  [MaxLength(4000)]
  public string? Description { get; set; }
}
