using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;
using DemoOrg.Data.Models;

namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(DocId), nameof(SortOrder)), Index(nameof(TenantId), nameof(ProductId))]
public sealed class EcomDocLine : AppBaseModel
{
  public long DocId { get; set; }
  public EcomDoc Doc { get; set; } = null!;

  public int SortOrder { get; set; }

  public long ProductId { get; set; }
  public OrgProduct Product { get; set; } = null!;

  [Precision(18, 3)]
  public decimal Qty { get; set; }

  [Precision(18, 2)]
  public decimal Price { get; set; }

  [Precision(5, 2)]
  public decimal DiscountPercent { get; set; }

  [Precision(18, 2)]
  public decimal Amount { get; set; }

  [Precision(18, 3)]
  public decimal QtyReserved { get; set; }

  [Precision(18, 3)]
  public decimal QtyPurchased { get; set; }

  [Precision(18, 3)]
  public decimal QtyDone { get; set; }

  [MaxLength(512)]
  public string? Note { get; set; }
}
