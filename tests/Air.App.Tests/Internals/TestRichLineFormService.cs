namespace Platform.App.Tests.Internals;

internal sealed class TestRichLineFormService(AppTestDbContext db, PlatformAppService app)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.DirectLines;

  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.Invoice)
      .AsReference<TestInvoiceGridService>()
      .AllowCreate<TestInvoiceFormService>();
    elements.Add(l => l.Title)
      .Required()
      .DisabledWhen(l => l.Priority == TestPriority.Urgent)
      .Unique("Duplicate title within tag", l => l.TagId);
    elements.Add(l => l.Tag)
      .AsReference<TestTagGridService>()
      .AllowCreate<TestInvoiceFormService>()
      .Where(t => t.CategoryId, l => l.InvoiceId)
      .PatchOnSelect(l => l.Amount, t => t.DefaultPrice)
      .Span(6);
    elements.Add(l => l.Amount).Span(6);
    elements.Add(l => l.HiddenNote).Hidden();
    elements.Add(l => l.Invoice!.Number);
    elements.Add(l => l.TierCode).AsSelect(
    [
      new OptionDto { Id = 1, Name = "T1" },
      new OptionDto { Id = 2, Name = "T2" }
    ]);
    elements.Add(l => l.Priority).AsSelect(
    [
      new OptionDto { Id = (int)TestPriority.Normal, Name = "Normal" },
      new OptionDto { Id = (int)TestPriority.Urgent, Name = "Urgent" }
    ]);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.Compute(ctx =>
    {
      if (ctx.Entity.Title == "FREE")
      {
        ctx.Entity.Amount = 0m;
      }
    });
  }
}
