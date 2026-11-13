namespace Platform.App.Tests.Internals;

internal sealed class TestAskWarmupGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoiceLine>(db, app)
{
  private static readonly long[] ExcludedCategories = [99L, 100L];

  public override Feature Feature => TestAppModule.AskWarmup;

  protected override void SetColumns()
  {
    Columns.UseForm<TestAskWarmupFormService>();
    Columns.UseStatusOf(new TestAskWarmupFlowService(db));
    Columns.Add(l => l.Title);
    Columns.Add(l => l.TierCode);
  }

  protected override void SetActions()
  {
    Actions.Add("AssignRichGridTag")
      .AllowEdit()
      .Ask<TestRichFilterAskInput>(els => els.Add(x => x.SecondaryTagId)
          .AsReference<TestTagGridService, TestTag>()
          .Where(t => t.Value == null && (t.CategoryId == null || !ExcludedCategories.Contains(t.CategoryId.Value))))
      .Do((_, _, _) => Task.CompletedTask);
  }
}
