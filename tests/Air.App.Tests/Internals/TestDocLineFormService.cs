namespace Platform.App.Tests.Internals;

internal sealed class TestDocLineFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.DocLines;

  protected override void SetParent() =>
    Parent.Bind<TestDocFormServiceBase>(l => l.InvoiceId);

  protected override void SetElements()
  {
    Elements.Add(l => l.InvoiceId);
    Elements.Add(l => l.Title).Required();
    Elements.Add(l => l.Amount);
    Elements.Add(l => l.TierCode);
  }

  protected override void SetActions()
  {
    Actions.Add("MoveToDoc")
      .AllowEdit()
      .Ask<TestAssignTagInput>(els => els.Add(x => x.TagId).Required())
      .Do((_, line, input, _) =>
      {
        line.InvoiceId = input.TagId;
        return Task.CompletedTask;
      });
  }
}
