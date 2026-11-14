namespace Platform.App.Tests.Internals;

internal sealed class TestDisabledWhenPatchLineFormService(AppTestDbContext db, PlatformAppService app)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.ConditionalPatchedLines;

  protected override void SetParent() => Parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetElements()
  {
    Elements.Add(l => l.Tag)
      .AsReference<TestTagGridService>()
      .DisabledWhen(l => l.TierCode == 99)
      .PatchOnSelect(l => l.Amount, t => t.DefaultPrice)
      .PatchOnSelect(l => l.Title, t => t.Name);
    Elements.Add(l => l.Amount).Disabled();
    Elements.Add(l => l.Title).Required().DisabledWhen(l => l.Priority == TestPriority.Urgent);
    Elements.Add(l => l.TierCode);
    Elements.Add(l => l.Priority).AsSelect(
    [
      new OptionDto { Id = (int)TestPriority.Draft, Name = "Draft" },
      new OptionDto { Id = (int)TestPriority.Normal, Name = "Normal" },
      new OptionDto { Id = (int)TestPriority.Urgent, Name = "Urgent" }
    ]);
  }
}
