namespace Platform.App.Tests.Internals;

internal sealed class TestDocLineFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.DocLines;

  protected override void SetParent(Parent parent) =>
    parent.Bind<TestDocFormServiceBase>(l => l.InvoiceId);

  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.InvoiceId);
    elements.Add(l => l.Title).Required();
    elements.Add(l => l.Amount);
    elements.Add(l => l.TierCode);
  }

  protected override void SetActions(Actions actions)
  {
    actions.Add("MoveToDoc")
      .AllowEdit()
      .Ask<TestAssignTagInput>(els => els.Add(x => x.TagId).Required())
      .Do((_, line, input, _) =>
      {
        line.InvoiceId = input.TagId;
        return Task.CompletedTask;
      });
  }
}
