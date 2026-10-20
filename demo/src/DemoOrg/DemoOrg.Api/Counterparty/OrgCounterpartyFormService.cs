namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.FullName);
    elements.Add(e => e.IsSupplier).AsCheckbox().Span(6);
    elements.Add(e => e.IsBuyer).AsCheckbox().Span(6);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Span(8);
    elements.Add(e => e.IsActive).AsCheckbox().Span(4);

    elements.AddTab("Requisites");
    elements.Add(e => e.TaxId).Unique("This tax id is already in use.").Span(4);
    elements.Add(e => e.VatNumber).Span(4);
    elements.Add(e => e.IsVatPayer).AsCheckbox().Span(4);
    elements.Add(e => e.Iban).Span(6);
    elements.Add(e => e.BankName).Span(6);

    elements.AddTab("Terms");
    elements.Add(e => e.PaymentDelayDays).Min(0).Span(6);
    elements.Add(e => e.DeliveryDays).Min(0).Span(6);

    elements.AddTab("Details");
    elements.Add(e => e.Address);
    elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    elements.Add(e => e.Email).AsEmail().Span(6);
    elements.Add(e => e.Note).AsTextarea();
  }

  protected override void SetRules(Rules rules)
  {
    var neither = L["A counterparty is a supplier, a buyer or both."];
    rules.For(e => e.IsSupplier).Must((e, isSupplier) => isSupplier || e.IsBuyer).WithMessage(neither).WithErrorCode("counterparty.role");
  }
}

