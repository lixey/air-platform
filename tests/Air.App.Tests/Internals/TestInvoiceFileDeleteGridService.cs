namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceFileDeleteGridService(
  AppTestDbContext db,
  PlatformAppService? app = null,
  bool refuseInAfterDelete = false)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetColumns() =>
    Columns.Add(i => i.Number);

  protected override void SetHooks()
  {
    Hooks.AllowMultiDelete();
    Hooks.DeleteFiles<TestAppFile>(f => f.InvoiceId);
    if (refuseInAfterDelete)
    {
      Hooks.AfterDelete(c =>
      {
        foreach (var rec in c.Records)
        {
          c.Refuse("Simulated post-delete refusal causing rollback.", rec.Id);
        }
      });
    }
  }
}
