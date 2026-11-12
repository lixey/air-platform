namespace Platform.App.Tests.Internals;

internal sealed class TestAskWarmupFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.AskWarmup;

  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.Title).Required();
    elements.Add(l => l.Amount);
  }

  protected override void SetActions(Actions actions)
  {
    actions.Add("AssignRichFormTag")
      .AllowEdit()
      .Ask<TestRichFilterAskInput>(els => els.Add(x => x.TagId)
          .AsReference<TestTagGridService, TestTag>()
          .Where(t => t.Name != "Blocked" && t.DefaultPrice > 5m && t.DefaultPrice < 900m && t.DefaultPrice <= 800m))
      .Do((_, _, _, _) => Task.CompletedTask);
  }
}
