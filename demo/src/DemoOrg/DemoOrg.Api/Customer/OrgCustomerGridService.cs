namespace DemoOrg.Customer;

public sealed class OrgCustomerGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCustomer>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgCustomerFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Phone).AsPhone();
    Columns.Add(r => r.Email).AsEmail();
    Columns.Add(r => r.City);
    Columns.Add(r => r.DiscountPercent).AsPercent();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.DeliveryType).AsReference<OrgDeliveryTypeGridService>();
    Columns.Add(r => r.PaymentMethod).AsReference<OrgPaymentMethodGridService>();
    Columns.Add(r => r.PaymentTerm).AsReference<OrgPaymentTermGridService>();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery()
  {
    Query.RecordLabel(e => e.Name);
    Query.SearchBy(r => r.Id, r => r.Name, r => r.Phone);
  }

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgCounterparty>(c => c.CustomerId, "The customer has a counterparty and cannot be deleted.");
}

