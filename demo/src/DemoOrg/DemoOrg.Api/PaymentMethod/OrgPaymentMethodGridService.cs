namespace DemoOrg.PaymentMethod;

public sealed class OrgPaymentMethodGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgPaymentMethod>(db, org)
{
  public override Feature Feature => Org.PaymentMethod;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgPaymentMethodFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.IsCash).AsCheckbox();
    Columns.Add(r => r.IsCashOnDelivery).AsCheckbox();
    Columns.Add(r => r.IsActive).AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgCustomer>(c => c.PaymentMethodId, "The payment method is used by customers and cannot be deleted.");
}

