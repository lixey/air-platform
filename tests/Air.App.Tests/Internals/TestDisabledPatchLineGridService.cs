namespace Platform.App.Tests.Internals;

internal sealed class TestDisabledPatchLineGridService(AppTestDbContext db, PlatformAppService app)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.PatchedLines;

  protected override void SetParent() => Parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetQuery()
  {
    Query.Compute((IReadOnlyList<GridComputedRow<TestInvoiceLine>> rows, CancellationToken _) =>
    {
      foreach (var r in rows)
      {
        r["computedBadge"] = $"badge:{r.Id}";
      }

      return Task.CompletedTask;
    });
  }

  protected override void SetColumns()
  {
    Columns.UseForm<TestDisabledPatchLineFormService>();
    Columns.Add(l => l.Tag).Editable();
    Columns.Add(l => l.Amount);
    Columns.Add(l => l.Title);
    Columns.Add(l => l.Priority);
    Columns.Add<string>("computedBadge").Disabled();
  }
}
