namespace DemoOrg.Customer;

public sealed class OrgCustomerContactFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCustomerContact>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override string Title => "Contact";

  protected override void SetParent() => Parent.Bind<OrgCustomerFormService>(c => c.CustomerId);

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus().Span(6);
    Elements.Add(e => e.Position).Span(6);
    Elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    Elements.Add(e => e.Email).AsEmail().Span(6);

    Elements.Add(e => e.IsPrimary).AsCheckbox();
    Elements.Add(e => e.Note).AsTextarea();
  }

  protected override void SetHooks()
  {
    Hooks.AfterSave(async (c, cancellationToken) =>
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
