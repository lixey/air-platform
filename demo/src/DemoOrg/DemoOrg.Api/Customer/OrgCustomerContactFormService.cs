namespace DemoOrg.Customer;

public sealed class OrgCustomerContactFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCustomerContact>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override string Title => "Contact";

  protected override void SetParent(Parent parent) => parent.Bind<OrgCustomerFormService>(c => c.CustomerId);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Span(6);
    elements.Add(e => e.Position).Span(6);
    elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    elements.Add(e => e.Email).AsEmail().Span(6);

    elements.Add(e => e.IsPrimary).AsCheckbox();
    elements.Add(e => e.Note).AsTextarea();
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterSave(async (c, cancellationToken) =>
    {
      if (!c.Entity.IsPrimary)
      {
        return;
      }

      var id = c.Entity.Id;
      var customer = c.Entity.CustomerId;
      var others = await db.Set<OrgCustomerContact>()
        .Where(contact => contact.CustomerId == customer && contact.Id != id && contact.IsPrimary)
        .ToListAsync(cancellationToken);
      foreach (var other in others)
      {
        other.IsPrimary = false;
      }
    });
  }
}
