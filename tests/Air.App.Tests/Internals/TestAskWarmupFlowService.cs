namespace Platform.App.Tests.Internals;

internal sealed class TestAskWarmupFlowService(AppTestDbContext db)
  : StatusFlowService<AppTestDbContext, TestInvoiceLine>(db)
{
  public override Feature Feature => TestAppModule.AskWarmup;

  protected override void SetFlow()
  {
    Flow.State(l => l.TierCode);
    Flow.Status(1, "Draft", UiColor.Slate);
    Flow.Status(2, "Ready", UiColor.Green);
    Flow.Initial(1);
    Flow.FollowedBy<TestInvoiceStatusFlow>(l => l.InvoiceId);
  }

  public Transition Promote => From(1)
    .To(2)
    .Ask<TestRichFilterAskInput>(els => els.Add(x => x.ThirdTagId)
        .AsReference<TestTagGridService, TestTag>()
        .Where(t => t.Entity == "FlowTag" && t.CategoryId != null && t.DefaultPrice >= 10m))
    .Check(_ => false, "Required");
}
