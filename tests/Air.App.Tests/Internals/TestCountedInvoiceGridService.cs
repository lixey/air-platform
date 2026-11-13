namespace Platform.App.Tests.Internals;

internal sealed class TestCountedInvoiceGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.CountedInvoices;

  protected override void SetColumns()
  {
    Columns.Add(x => x.Id);
    Columns.Add(x => x.Number);
    Columns.Add(x => x.AuditedTotal);
  }

  protected override void SetQuery()
  {
    Query.Where(x => !x.IsHidden);
    Query.NamedFilter("stage", "Stage")
      .Inline()
      .ShowCounts()
      .Item("open", "Open", x => x.Status == 1)
      .Default()
      .Item("closed", "Closed", x => x.Status == 2)
      .Item("archived", "Archived", x => x.Status == 3)
      .ShowCount(false)
      .All("All");
    Query.NamedFilter("highValue", "High value", x => x.AuditedTotal >= 100m)
      .ShowCount();
  }

  protected override void SetHooks()
  {
    Hooks.AllowMultiDelete();
    Hooks.PreventDeleteWhenUsedBy<TestInvoiceTag>(j => j.InvoiceId, "Invoice is referenced by tags.");
  }
}
