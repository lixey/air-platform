namespace Platform.App.Tests.Internals;

internal sealed class TestDisabledPatchLineGridService(AppTestDbContext db, PlatformAppService app)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.PatchedLines;

  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetQuery(Query query)
  {
    query.Compute((IReadOnlyList<GridComputedRow<TestInvoiceLine>> rows, CancellationToken _) =>
    {
      foreach (var r in rows)
      {
        r["computedBadge"] = $"badge:{r.Id}";
      }

      return Task.CompletedTask;
    });
  }

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestDisabledPatchLineFormService>();
    columns.Add(l => l.Tag).Editable();
    columns.Add(l => l.Amount);
    columns.Add(l => l.Title);
    columns.Add(l => l.Priority);
    columns.Add<string>("computedBadge").Disabled();
  }
}
