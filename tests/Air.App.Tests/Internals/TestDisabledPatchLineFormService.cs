namespace Platform.App.Tests.Internals;

internal sealed class TestDisabledPatchLineFormService(AppTestDbContext db, PlatformAppService app)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.PatchedLines;

  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.Tag)
      .AsReference<TestTagGridService>()
      .PatchOnSelect(l => l.Amount, t => t.DefaultPrice)
      .PatchOnSelect(l => l.HiddenNote, t => t.Name, onlyWhenEmpty: true)
      .PatchOnSelect(l => l.Title, t => t.Name, onlyWhenEmpty: true);
    elements.Add(l => l.Amount).Disabled();
    elements.Add(l => l.HiddenNote).Disabled();
    elements.Add(l => l.Title).Required();
    elements.Add(l => l.Priority).AsSelect(
    [
      new OptionDto { Id = (int)TestPriority.Draft, Name = "Draft" },
      new OptionDto { Id = (int)TestPriority.Normal, Name = "Normal" },
      new OptionDto { Id = (int)TestPriority.Urgent, Name = "Urgent" }
    ]);
  }
}
