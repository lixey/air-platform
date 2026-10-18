namespace DemoOrg.PaymentMethod;

public sealed class OrgPaymentMethodGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgPaymentMethod>(db, org)
{
  public override Feature Feature => Org.PaymentMethod;

  protected override void SetColumns(Columns<OrgPaymentMethod> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgPaymentMethodFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.IsCash).AsCheckbox();
    columns.Add(r => r.IsCashOnDelivery).AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks)
  {
    var inUse = L["The payment method is used by customers and cannot be deleted."];
    hooks.ValidateDelete(async (c, cancellationToken) =>
    {
      var selected = c.Records.Select(r => (long?)r.Id).ToList();
      var used = await db.OrgCustomer.Where(customer => selected.Contains(customer.PaymentMethodId))
        .Select(customer => customer.PaymentMethodId!.Value).Distinct().ToListAsync(cancellationToken);
      foreach (var id in used)
      {
        c.AddError("paymentMethod.used", inUse, id);
      }
    });
  }
}

