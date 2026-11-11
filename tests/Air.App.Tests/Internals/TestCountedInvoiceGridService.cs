namespace Platform.App.Tests.Internals;

internal sealed class TestCountedInvoiceGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.CountedInvoices;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(x => x.Id);
    columns.Add(x => x.Number);
    columns.Add(x => x.AuditedTotal);
  }

  protected override void SetQuery(Query query)
  {
    query.Where(x => !x.IsHidden);
    query.NamedFilter("stage", "Stage")
      .Inline()
      .ShowCounts()
      .Item("open", "Open", x => x.Status == 1)
      .Default()
      .Item("closed", "Closed", x => x.Status == 2)
      .Item("archived", "Archived", x => x.Status == 3)
      .ShowCount(false)
      .All("All");
    query.NamedFilter("highValue", "High value", x => x.AuditedTotal >= 100m)
      .ShowCount();
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AllowMultiDelete();
    hooks.PreventDeleteWhenUsedBy<TestInvoiceTag>(j => j.InvoiceId, "Invoice is referenced by tags.");
  }
}
