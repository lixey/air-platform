namespace DemoOrg.Customer;

public sealed class OrgCustomerGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCustomer>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetColumns(Columns<OrgCustomer> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCustomerFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Phone).AsPhone();
    columns.Add(r => r.Email).AsEmail();
    columns.Add(r => r.City);
    columns.Add(r => r.DiscountPercent).AsPercent();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.DeliveryType).AsReference<OrgDeliveryTypeGridService>();
    columns.Add(r => r.PaymentMethod).AsReference<OrgPaymentMethodGridService>();
    columns.Add(r => r.PaymentTerm).AsReference<OrgPaymentTermGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query)
  {
    query.RecordLabel(e => e.Name);
    query.SearchBy(r => r.Id, r => r.Name, r => r.Phone);
  }

  protected override void SetHooks(Hooks hooks) =>
    hooks.PreventDeleteWhenUsedBy<OrgCounterparty>(c => c.CustomerId, "The customer has a counterparty and cannot be deleted.");
}

