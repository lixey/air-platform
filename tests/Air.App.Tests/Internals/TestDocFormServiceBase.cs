namespace Platform.App.Tests.Internals;

internal abstract class TestDocFormServiceBase(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  protected abstract long DocStatusKind { get; }

  protected override void SetQuery(Query query) =>
    query.From(db.Invoices).Where(d => !d.IsHidden && d.Status == DocStatusKind);

  protected override void SetElements(Elements elements)
  {
    elements.Add(d => d.Number).Required();
    elements.AddChild<TestDocLineGridService, TestInvoiceLine>()
      .SumTo(d => d.AuditedTotal, l => l.Amount)
      .SumTo(d => d.PrintedCount, l => l.TierCode);
  }

  protected override Task RecalculateAsync(TestInvoice entity, CancellationToken ct)
  {
    entity.CachedLineTotal = entity.AuditedTotal * 2m;
    return Task.CompletedTask;
  }
}
