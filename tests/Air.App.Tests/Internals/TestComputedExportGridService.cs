namespace Platform.App.Tests.Internals;

internal sealed class TestComputedExportGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public int ComputeBatchCalls { get; private set; }

  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetQuery(Query query)
  {
    query.SortBy(i => i.Id);
    query.Compute(async (IReadOnlyList<GridComputedRow<TestInvoice>> rows, CancellationToken ct) =>
    {
      ComputeBatchCalls++;
      var tagCount = await db.Tags.AsNoTracking().CountAsync(ct);
      foreach (var row in rows)
      {
        row["computedSummary"] = $"{row.Record.Number}:{tagCount}";
      }
    });
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(i => i.Number);
    columns.Add<string>("computedSummary").Computed().Label("Summary");
  }
}
