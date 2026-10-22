namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(TaxId), IsUnique = true)]
public sealed class OrgCounterparty : AppBaseModel
{
  [Required, MaxLength(256)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(512)]
  public string? FullName { get; set; }

  [MaxLength(16)]
  public string? TaxId { get; set; }

  [MaxLength(16)]
  public string? VatNumber { get; set; }

  public bool IsVatPayer { get; set; }

  public bool IsSupplier { get; set; }

  public bool IsBuyer { get; set; }

  public long? CustomerId { get; set; }
  public OrgCustomer? Customer { get; set; }

  public int PaymentDelayDays { get; set; }

  public int DeliveryDays { get; set; }

  [MaxLength(34)]
  public string? Iban { get; set; }

  [MaxLength(128)]
  public string? BankName { get; set; }

  [MaxLength(512)]
  public string? Address { get; set; }

  [MaxLength(32)]
  public string? Phone { get; set; }

  [MaxLength(256)]
  public string? Email { get; set; }

  public bool IsActive { get; set; } = true;

  [MaxLength(2000)]
  public string? Note { get; set; }
}
