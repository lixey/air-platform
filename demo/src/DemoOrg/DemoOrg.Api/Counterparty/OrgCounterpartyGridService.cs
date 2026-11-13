namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgCounterpartyFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.TaxId);
    Columns.Add(r => r.IsSupplier).AsCheckbox();
    Columns.Add(r => r.IsBuyer).AsCheckbox();
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.PaymentDelayDays);
    Columns.Add(r => r.DeliveryDays);
    Columns.Add(r => r.Phone).AsPhone();
    Columns.Add(r => r.Email).AsEmail();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgProduct>(p => p.SupplierId, "The counterparty supplies products and cannot be deleted.");
}

