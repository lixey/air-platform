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
    Query.NamedFilter("Stage", "Stage")
      .Inline()
      .ShowCounts()
      .Item("Open", "Open", x => x.Status == 1)
      .Default()
      .Item("Closed", "Closed", x => x.Status == 2)
      .Item("Archived", "Archived", x => x.Status == 3)
      .ShowCount(false)
      .All("All");
    Query.NamedFilter("HighValue", "High value", x => x.AuditedTotal >= 100m)
      .ShowCount();
  }

  protected override void SetHooks()
  {
    Hooks.AllowMultiDelete();
    Hooks.PreventDeleteWhenUsedBy<TestInvoiceTag>(j => j.InvoiceId, "Invoice is referenced by tags.");
  }
}
