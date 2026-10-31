namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLineFormService(AppTestDbContext db, PlatformAppService app) : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);
  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.Title).Required();
    elements.Add(l => l.Amount);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeSave((ctx, _) =>
    {
      var parentInvoice = ParentRecord<TestInvoice>()
        ?? throw new InvalidOperationException("Parent invoice was not preloaded.");
      if (!ctx.Entity.Title.StartsWith(parentInvoice.Number + ":", StringComparison.Ordinal))
      {
        ctx.Entity.Title = $"{parentInvoice.Number}:{ctx.Entity.Title}";
      }

      return Task.CompletedTask;
    });
  }
}
