using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Counterparty;
using DemoOrg.Data.Models;

namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderFormService(EcomDbContext db, EcomPurchaseOrderFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.PurchaseOrderId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomPurchaseOrderGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(4);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(4);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(4);
    elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);
    elements.Add(e => e.DueDate).Span(4);
    elements.Add(e => e.ExternalNumber).Span(4);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomPurchaseOrderLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomPurchaseOrderBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomPurchaseOrderFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomPurchaseOrderHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules(Rules rules)
  {
    var texts = EcomDocTexts.Of(L);
    rules.For(e => e.CounterpartyId)
      .MustAsync((doc, supplier, cancellationToken) => EcomDocRules.SupplierOfLinesAsync(db, doc.Id, supplier, cancellationToken))
      .WithMessage(texts.ForeignLines);
  }
}
