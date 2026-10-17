using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Phone))]
public sealed class OrgCustomer : AppBaseModel
{
  [Required, MaxLength(256)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(32)]
  public string? Phone { get; set; }

  [MaxLength(256)]
  public string? Email { get; set; }

  [MaxLength(128)]
  public string? City { get; set; }

  [Precision(5, 2)]
  public decimal DiscountPercent { get; set; }

  public long? ManagerId { get; set; }
  public AppUser? Manager { get; set; }

  public long? DeliveryTypeId { get; set; }
  public OrgDeliveryType? DeliveryType { get; set; }

  public long? PaymentMethodId { get; set; }
  public OrgPaymentMethod? PaymentMethod { get; set; }

  public long? PaymentTermId { get; set; }
  public OrgPaymentTerm? PaymentTerm { get; set; }

  public bool IsActive { get; set; } = true;

  [MaxLength(2000)]
  public string? Note { get; set; }
}
