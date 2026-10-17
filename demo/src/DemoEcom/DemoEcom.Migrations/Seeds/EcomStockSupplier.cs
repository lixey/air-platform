namespace DemoEcom.Migrations.Seeds;

/// <summary>An active supplier: its key, the days it gives to pay and the days it delivers in.</summary>
internal sealed record EcomStockSupplier(long Id, int PaymentDelayDays, int DeliveryDays);
