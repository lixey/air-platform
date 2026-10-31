namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLineGridService(AppTestDbContext db, PlatformAppService app) : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);
  protected override void SetHooks(Hooks hooks)
  {
    hooks.AllowMultiDelete();
    hooks.AfterDelete(async (ctx, ct) =>
    {
      foreach (var invId in ctx.Records.Select(r => r.InvoiceId).Distinct())
      {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invId, ct);
        inv?.CachedLineTotal = await db.InvoiceLines.Where(l => l.InvoiceId == invId).SumAsync(l => l.Amount, ct);
      }
    });
    hooks.AfterRestore(async (ctx, ct) =>
    {
      foreach (var invId in ctx.Records.Select(r => r.InvoiceId).Distinct())
      {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invId, ct);
        inv?.CachedLineTotal = await db.InvoiceLines.Where(l => l.InvoiceId == invId).SumAsync(l => l.Amount, ct);
      }
    });
  }

  protected override void SetNav(Nav nav)
  {
    nav.AddCreate<TestInvoiceLineFormService>();
    nav.AddPicker<TestTagGridService>(AttachTag);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestInvoiceLineFormService>();
    columns.Add(l => l.Title);
    columns.Add(l => l.Amount).Sum();
  }

  public Task<GridActionDto> AttachTag(GridPickRequest req, CancellationToken ct = default) =>
    AttachPickedAsync<TestTag>(req, (tag, line) =>
    {
      line.Title = tag.Name;
      line.Amount = 10m;
    }, ct);
}
