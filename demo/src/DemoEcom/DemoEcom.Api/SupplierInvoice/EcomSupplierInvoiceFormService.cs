using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Counterparty;
using DemoOrg.Data.Models;

namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceFormService(EcomDbContext db, EcomSupplierInvoiceFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.SupplierInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomSupplierInvoiceGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(4);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(4);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(4);
    elements.Add(e => e.DueDate).Span(6);
    elements.Add(e => e.ExternalNumber).Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomSupplierInvoiceLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomSupplierInvoiceBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomSupplierInvoiceFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSupplierInvoiceHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules(Rules rules)
  {
    var texts = EcomDocTexts.Of(L);
    rules.For(e => e.CounterpartyId)
      .MustAsync((doc, supplier, cancellationToken) => EcomDocRules.SupplierOfLinesAsync(db, doc.Id, supplier, cancellationToken))
      .WithMessage(texts.ForeignLines);
  }
}
