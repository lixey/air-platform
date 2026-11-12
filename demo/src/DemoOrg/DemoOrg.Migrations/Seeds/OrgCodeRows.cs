namespace DemoOrg.Migrations.Seeds;

public static class OrgCodeRows
{
  public static OrgCurrency[] Currencies() =>
  [
    new() { Id = OrgCurrencyData.UahId, Handle = OrgCurrencyData.Uah, Code = OrgCurrencyData.UahCode, Name = "Ukrainian Hryvnia", Symbol = "\u20b4", IsActive = true },
    new() { Id = OrgCurrencyData.UsdId, Handle = OrgCurrencyData.Usd, Code = OrgCurrencyData.UsdCode, Name = "US Dollar", Symbol = "$", IsActive = true },
    new() { Id = OrgCurrencyData.EurId, Handle = OrgCurrencyData.Eur, Code = OrgCurrencyData.EurCode, Name = "Euro", Symbol = "\u20ac", IsActive = true }
  ];

  public static OrgUnit[] Units() =>
  [
    new() { Id = OrgUnitData.PieceId, Handle = OrgUnitData.Piece, Name = "Pieces", IsActive = true },
    new() { Id = OrgUnitData.SetId, Handle = OrgUnitData.Set, Name = "Sets", IsActive = true },
    new() { Id = OrgUnitData.LitreId, Handle = OrgUnitData.Litre, Name = "Litres", IsActive = true },
    new() { Id = OrgUnitData.KilogramId, Handle = OrgUnitData.Kilogram, Name = "Kilograms", IsActive = true },
    new() { Id = OrgUnitData.MetreId, Handle = OrgUnitData.Metre, Name = "Metres", IsActive = true }
  ];

  public static OrgPaymentTerm[] PaymentTerms() =>
  [
    new() { Id = OrgPaymentTermData.FullPrepaymentId, Handle = OrgPaymentTermData.FullPrepayment, Name = "Full Prepayment", IsActive = true },
    new() { Id = OrgPaymentTermData.PartialPrepaymentId, Handle = OrgPaymentTermData.PartialPrepayment, Name = "Partial Prepayment", IsActive = true },
    new() { Id = OrgPaymentTermData.PaymentOnDeliveryId, Handle = OrgPaymentTermData.PaymentOnDelivery, Name = "Payment on Delivery", IsActive = true },
    new() { Id = OrgPaymentTermData.NoPaymentId, Handle = OrgPaymentTermData.NoPayment, Name = "No Payment Required", IsActive = true }
  ];

  public static async Task SyncAsync(OrgDbContext db, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, Currencies(), cancellationToken);
    await CodeRows.SyncAsync(db, Units(), cancellationToken);
    await CodeRows.SyncAsync(db, PaymentTerms(), cancellationToken);
  }
}
