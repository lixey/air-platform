namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnFormService(EcomDbContext db, EcomSupplierReturnFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SupplierReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetElements()
  {
    Elements.UseRecordLabelOf<EcomSupplierReturnGridService>();
    Elements.Add(e => e.Number).AsInfo().Span(3);
    Elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    Elements.Disable(Frozen);
    Elements.Add(e => e.Date).Span(3);
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    Elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(4);
    Elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(4);
    Elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(4);
    Elements.Add(e => e.Note).AsTextarea();

    Elements.AddChild<EcomSupplierReturnLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    Elements.AddChild<EcomSupplierReturnBasisGridService>().Key("basis").Label("Entered on");
    Elements.AddChild<EcomSupplierReturnHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules()
  {
    var texts = EcomDocTexts.Of(L);
    Rules.For(e => e.CounterpartyId)
      .MustAsync((doc, supplier, cancellationToken) => EcomDocRules.SupplierOfLinesAsync(db, doc.Id, supplier, cancellationToken))
      .WithMessage(texts.ForeignLines);
  }
}
