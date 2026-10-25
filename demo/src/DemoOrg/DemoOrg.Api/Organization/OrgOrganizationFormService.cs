namespace DemoOrg.Organization;

public sealed class OrgOrganizationFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgOrganization>(db, org)
{
  public override Feature Feature => Org.Organization;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus().Unique("This name is already in use.").Span(8);
    elements.Add(e => e.Code).Unique("This code is already in use.").Span(4);
    elements.Add(e => e.FullName);
    elements.Add(e => e.TaxId).Span(4);
    elements.Add(e => e.VatNumber).Span(4);
    elements.Add(e => e.IsVatPayer).AsCheckbox().Span(4);
    elements.Add(e => e.CurrencyId).AsReference<OrgCurrencyGridService>().Disabled(!IsNew).Span(3);
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>().Span(3);
    elements.Add(e => e.IsActive).AsCheckbox().Span(3);
    elements.Add(e => e.SortOrder).Span(3);
    elements.AddSeparator("Details");
    elements.Add(e => e.Address);
    elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    elements.Add(e => e.Email).AsEmail().Span(6);
    elements.Add(e => e.Note).AsTextarea();
  }
}

