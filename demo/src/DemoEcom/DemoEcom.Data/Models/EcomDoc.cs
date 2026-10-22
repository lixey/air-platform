namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(OrganizationId), nameof(DocTypeId), nameof(Number), IsUnique = true)]
[Index(nameof(TenantId), nameof(DocTypeId), nameof(Date))]
[Index(nameof(TenantId), nameof(Date))]
[Index(nameof(TenantId), nameof(CustomerId))]
[Index(nameof(TenantId), nameof(CounterpartyId))]
public sealed class EcomDoc : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  public long DocTypeId { get; set; }
  public EcomDocType DocType { get; set; } = null!;

  [KeepsVersion]
  public long DocStatusId { get; set; }
  public EcomDocStatus DocStatus { get; set; } = null!;

  [MaxLength(32)]
  public string Number { get; set; } = string.Empty;

  public DateOnly Date { get; set; }

  public long? CustomerId { get; set; }
  public OrgCustomer? Customer { get; set; }

  public long? ContactId { get; set; }
  public OrgCustomerContact? Contact { get; set; }

  public long? CounterpartyId { get; set; }
  public OrgCounterparty? Counterparty { get; set; }

  public long? WarehouseId { get; set; }
  public OrgWarehouse? Warehouse { get; set; }

  public long? WarehouseToId { get; set; }
  public OrgWarehouse? WarehouseTo { get; set; }

  public long? MoneyAccountId { get; set; }
  public OrgMoneyAccount? MoneyAccount { get; set; }

  public long? DeliveryTypeId { get; set; }
  public OrgDeliveryType? DeliveryType { get; set; }

  public long? PaymentMethodId { get; set; }
  public OrgPaymentMethod? PaymentMethod { get; set; }

  public long? PaymentTermId { get; set; }
  public OrgPaymentTerm? PaymentTerm { get; set; }

  public long? ManagerId { get; set; }
  public AppUser? Manager { get; set; }

  public long CurrencyId { get; set; }
  public OrgCurrency Currency { get; set; } = null!;

  [Precision(18, 6)]
  public decimal Rate { get; set; } = 1m;

  [Precision(18, 3)]
  public decimal Qty { get; set; }

  [Precision(18, 2)]
  public decimal Amount { get; set; }

  [Precision(18, 2)]
  public decimal PaidAmount { get; set; }

  public DateOnly? DueDate { get; set; }

  [MaxLength(64)]
  public string? ExternalNumber { get; set; }

  [MaxLength(256)]
  public string? RecipientName { get; set; }

  [MaxLength(32)]
  public string? RecipientPhone { get; set; }

  [MaxLength(512)]
  public string? DeliveryAddress { get; set; }

  [MaxLength(64)]
  public string? TrackingNumber { get; set; }

  [MaxLength(2000)]
  public string? Note { get; set; }

  public DateTimeOffset? PostedAt { get; set; }
}
