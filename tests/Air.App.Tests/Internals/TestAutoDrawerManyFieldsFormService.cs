namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerManyFieldsFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetElements(Elements elements)
  {
    elements.Add(i => i.Id).Disabled();
    elements.Add(i => i.Number).Required();
    elements.Add(i => i.Status);
    elements.Add(i => i.IsHidden);
    elements.Add(i => i.PrintedCount);
    elements.Add(i => i.InternalMemo);
    elements.Add(i => i.ApiSecret);
    elements.Add(i => i.CachedLineTotal);
    elements.Add(i => i.AuditedTotal);
  }
}
