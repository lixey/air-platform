using Air.Data;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg.Migrations.Seeds;

public static class OrgCodeRows
{
  public static OrgCurrency[] Currencies() =>
  [
    new() { Id = OrgData.Currency.UahId, Handle = OrgData.Currency.Uah, Name = "Ukrainian Hryvnia", Symbol = "₴", IsActive = true },
    new() { Id = OrgData.Currency.UsdId, Handle = OrgData.Currency.Usd, Name = "US Dollar", Symbol = "$", IsActive = true },
    new() { Id = OrgData.Currency.EurId, Handle = OrgData.Currency.Eur, Name = "Euro", Symbol = "€", IsActive = true }
  ];

  public static OrgUnit[] Units() =>
  [
    new() { Id = OrgData.Unit.PieceId, Handle = OrgData.Unit.Piece, Name = "Pieces", IsActive = true },
    new() { Id = OrgData.Unit.SetId, Handle = OrgData.Unit.Set, Name = "Sets", IsActive = true },
    new() { Id = OrgData.Unit.LitreId, Handle = OrgData.Unit.Litre, Name = "Litres", IsActive = true },
    new() { Id = OrgData.Unit.KilogramId, Handle = OrgData.Unit.Kilogram, Name = "Kilograms", IsActive = true },
    new() { Id = OrgData.Unit.MetreId, Handle = OrgData.Unit.Metre, Name = "Metres", IsActive = true }
  ];

  public static OrgPaymentTerm[] PaymentTerms() =>
  [
    new() { Id = OrgData.PaymentTerm.FullPrepaymentId, Handle = OrgData.PaymentTerm.FullPrepayment, Name = "Full Prepayment", IsActive = true },
    new() { Id = OrgData.PaymentTerm.PartialPrepaymentId, Handle = OrgData.PaymentTerm.PartialPrepayment, Name = "Partial Prepayment", IsActive = true },
    new() { Id = OrgData.PaymentTerm.PaymentOnDeliveryId, Handle = OrgData.PaymentTerm.PaymentOnDelivery, Name = "Payment on Delivery", IsActive = true },
    new() { Id = OrgData.PaymentTerm.NoPaymentId, Handle = OrgData.PaymentTerm.NoPayment, Name = "No Payment Required", IsActive = true }
  ];

  public static async Task SyncAsync(OrgDbContext db, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, Currencies(), cancellationToken);
    await CodeRows.SyncAsync(db, Units(), cancellationToken);
    await CodeRows.SyncAsync(db, PaymentTerms(), cancellationToken);
  }
}
