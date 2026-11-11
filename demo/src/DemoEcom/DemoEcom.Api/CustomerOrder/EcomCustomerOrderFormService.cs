namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderFormService(
  EcomDbContext db,
  EcomCustomerOrderFlowService flow,
  EcomService? ecom = null
) : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.CustomerOrderId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    await base.SetElementsAsync(elements, cancellationToken);

    elements.UseRecordLabelOf<EcomCustomerOrderGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);

    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(3);
    elements.Add(e => e.Contact).AsReference<OrgCustomerContactGridService>()
      .Where(c => c.CustomerId, e => e.CustomerId).Span(3);
    elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(3);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(3);

    elements.AddSeparator("Delivery and payment");
    elements.Add(e => e.DeliveryTypeId).AsReference<OrgDeliveryTypeGridService>().Span(4);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(4);
    elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);

    elements.Add(e => e.DueDate).Span(6);
    elements.Add(e => e.ExternalNumber).Span(6);
    elements.Add(e => e.RecipientName).Span(6);
    elements.Add(e => e.RecipientPhone).AsPhone("+380 (##) ###-##-##").Span(6);
    elements.Add(e => e.DeliveryAddress).Span(8);
    elements.Add(e => e.TrackingNumber).Span(4);
    elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(12);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomCustomerOrderLineGridService>().Key("lines").Label("Lines").SumTo("amount", "amount");
    elements.AddChild<EcomCustomerOrderFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomCustomerOrderHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeSave(c =>
    {
      if (c.Entity.DueDate.HasValue && c.Entity.DueDate.Value < c.Entity.Date)
      {
        c.Warn(L["Due date is earlier than the order date."], path: "dueDate");
      }
    });
  }
}
