namespace DemoEcom.Migrations.Seeds.Internals;

internal sealed record EcomExampleCustomer(
  long Id,
  string Name,
  string? Phone,
  string? City,
  decimal DiscountPercent,
  long? ManagerId,
  long? DeliveryTypeId,
  long? PaymentMethodId,
  long? PaymentTermId
);
