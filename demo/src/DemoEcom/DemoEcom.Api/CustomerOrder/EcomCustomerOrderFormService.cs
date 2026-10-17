using Air.App.Metadata;
using DemoApp.User;
using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Counterparty;
using DemoOrg.Customer;
using DemoOrg.Data.Models;
using DemoOrg.DeliveryType;
using DemoOrg.Organization;
using DemoOrg.PaymentMethod;
using DemoOrg.PaymentTerm;
using DemoOrg.Warehouse;

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

    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);

    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(3);
    elements.Add(e => e.ContactId).AsReference<OrgCustomerContactGridService, OrgCustomerContact>().Span(3);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>().Required().Span(3);
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

    elements.AddChild<EcomCustomerOrderLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomCustomerOrderFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomCustomerOrderHistoryGridService>().Key("history").Label("History");
  }
}
