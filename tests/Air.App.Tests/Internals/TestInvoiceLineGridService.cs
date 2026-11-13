namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLineGridService(AppTestDbContext db, PlatformAppService app) : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetParent() => Parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);
  protected override void SetHooks()
  {
    Hooks.AllowMultiDelete();
    Hooks.AfterDelete(async (ctx, ct) =>
    {
      foreach (var invId in ctx.Records.Select(r => r.InvoiceId).Distinct())
      {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invId, ct);
        inv?.CachedLineTotal = await db.InvoiceLines.Where(l => l.InvoiceId == invId).SumAsync(l => l.Amount, ct);
      }
    });
  }

  protected override void SetNav()
  {
    Nav.AddCreate<TestInvoiceLineFormService>();
    Nav.AddPicker<TestTagGridService>(AttachTag);
  }

  protected override void SetColumns()
  {
    Columns.UseForm<TestInvoiceLineFormService>();
    Columns.Add(l => l.Title);
    Columns.Add(l => l.Amount).Sum();
  }

  [RequireAction(Right.Edit)]
  public Task<GridActionDto> AttachTag(GridPickRequest req, CancellationToken ct = default) =>
    AttachPickedAsync<TestTag>(req, (tag, line) =>
    {
      line.Title = tag.Name;
      line.Amount = 10m;
    }, ct);
}
