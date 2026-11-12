namespace Platform.App.Tests.Internals;

internal sealed class TestAskWarmupFlowService(AppTestDbContext db)
  : StatusFlowService<AppTestDbContext, TestInvoiceLine>(db)
{
  public override Feature Feature => TestAppModule.AskWarmup;

  protected override void SetFlow(Flow flow)
  {
    flow.State(l => l.TierCode);
    flow.Status(1, "Draft", UiColor.Slate);
    flow.Status(2, "Ready", UiColor.Green);
    flow.Initial(1);
    flow.FollowedBy<TestInvoiceStatusFlow>(l => l.InvoiceId);
  }

  public Transition Promote => From(1)
    .To(2)
    .Ask<TestRichFilterAskInput>(els => els.Add(x => x.ThirdTagId)
        .AsReference<TestTagGridService, TestTag>()
        .Where(t => t.Entity == "FlowTag" && t.CategoryId != null && t.DefaultPrice >= 10m))
    .Check(_ => false, "Required");
}
