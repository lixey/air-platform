namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerManyFieldsFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetElements()
  {
    Elements.Add(i => i.Id).Disabled();
    Elements.Add(i => i.Number).Required();
    Elements.Add(i => i.Status);
    Elements.Add(i => i.IsHidden);
    Elements.Add(i => i.PrintedCount);
    Elements.Add(i => i.InternalMemo);
    Elements.Add(i => i.ApiSecret);
    Elements.Add(i => i.CachedLineTotal);
    Elements.Add(i => i.AuditedTotal);
  }
}
