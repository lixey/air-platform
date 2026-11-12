namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnFormService(EcomDbContext db, EcomSupplierReturnFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SupplierReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomSupplierReturnGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(4);
    elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(4);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(4);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomSupplierReturnLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    elements.AddChild<EcomSupplierReturnBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomSupplierReturnHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules(Rules rules)
  {
    var texts = EcomDocTexts.Of(L);
    rules.For(e => e.CounterpartyId)
      .MustAsync((doc, supplier, cancellationToken) => EcomDocRules.SupplierOfLinesAsync(db, doc.Id, supplier, cancellationToken))
      .WithMessage(texts.ForeignLines);
  }
}
