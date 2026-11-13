namespace Platform.App.Tests.Internals;

internal sealed class TestOrderFlowService(AppTestDbContext db, List<string>? events = null)
  : StatusFlowService<AppTestDbContext, TestInvoice>(db)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetFlow()
  {
    Flow.State(x => x.Status);
    Flow.Status(1, "Open", UiColor.Blue);
    Flow.Status(2, "Completed", UiColor.Green);
    Flow.FinalStatus(3, "Canceled", UiColor.Red);
    Flow.Initial(1);
  }

  public ChildRule Lines => Children(FlowOf<TestOrderLineFlowService>(), l => l.InvoiceId)
    .Give(Complete, [2])
    .Do((Follow _, CancellationToken _) =>
    {
      events?.Add("FollowEffect:Lines");
      return Task.CompletedTask;
    });

  protected override Task LockAsync(IMutationContext context, IReadOnlyList<TestInvoice> records, CancellationToken ct)
  {
    events?.Add($"FlowLock:{string.Join(',', records.Select(r => r.Id))}");
    return Task.CompletedTask;
  }

  public Transition Complete => From(1).To(2).Do((_, _) =>
  {
    events?.Add("TransitionEffect:Complete");
    return Task.CompletedTask;
  });

  public Transition Cancel => FromOpen().To(3);
}
