namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.FullName);
    Elements.Add(e => e.IsSupplier).AsCheckbox().Span(6);
    Elements.Add(e => e.IsBuyer).AsCheckbox().Span(6);
    Elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Span(8);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(4);

    Elements.AddTab("Requisites");
    Elements.Add(e => e.TaxId).Unique("This tax id is already in use.").Span(4);
    Elements.Add(e => e.VatNumber).Span(4);
    Elements.Add(e => e.IsVatPayer).AsCheckbox().Span(4);
    Elements.Add(e => e.Iban).Span(6);
    Elements.Add(e => e.BankName).Span(6);

    Elements.AddTab("Terms");
    Elements.Add(e => e.PaymentDelayDays).Min(0).Span(6);
    Elements.Add(e => e.DeliveryDays).Min(0).Span(6);

    Elements.AddTab("Details");
    Elements.Add(e => e.Address);
    Elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    Elements.Add(e => e.Email).AsEmail().Span(6);
    Elements.Add(e => e.Note).AsTextarea();
  }

  protected override void SetRules()
  {
    var neither = L["A counterparty is a supplier, a buyer or both."];
    Rules.For(e => e.IsSupplier).Must((e, isSupplier) => isSupplier || e.IsBuyer).WithMessage(neither).WithErrorCode("counterparty.role");
  }
}

