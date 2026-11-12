namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long New = EcomDocStatusData.CustomerOrderNewId;
  private const long InProgress = EcomDocStatusData.CustomerOrderInProgressId;
  private const long Confirmed = EcomDocStatusData.CustomerOrderConfirmedId;
  private const long Paused = EcomDocStatusData.CustomerOrderPausedId;
  private const long Completed = EcomDocStatusData.CustomerOrderCompletedId;
  private const long Canceled = EcomDocStatusData.CustomerOrderCanceledId;

  protected override long Type => EcomDocTypeData.CustomerOrderId;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetFlow(Flow flow)
  {
    base.SetFlow(flow);
    flow.Status(New, "New", UiColor.Sky);
    flow.Status(InProgress, "In progress", UiColor.Amber);
    flow.Status(Confirmed, "Confirmed", UiColor.Blue);
    flow.Status(Paused, "Paused", UiColor.Amber);
    flow.FinalStatus(Completed, "Completed", UiColor.Green);
    flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
    flow.Initial(New);
  }

  private Expression<Func<EcomDoc, bool>> HasLines => order => Db.EcomDocLine.Any(line => line.DocId == order.Id);

  private const string NoLines = "An order without lines takes no status.";

  public Transition Reopen => FromOpen().To(New).Do(PostAsync);

  public Transition Start => FromOpen().To(InProgress).Do(PostAsync);

  public Transition Confirm => FromOpen().To(Confirmed).When(HasLines, NoLines).Do(PostAsync);

  public Transition Pause => FromOpen().To(Paused).When(HasLines, NoLines).Do(PostAsync);

  public Transition Complete => FromOpen().To(Completed).When(HasLines, NoLines).Do(PostAsync);

  public Transition Cancel => FromOpen().To(Canceled)
    .Confirm(Texts.CancelConfirm)
    .Ask<EcomDocCancelInput>(input => EcomDocActions.AskReason(input, r => r.Reason), note: r => r.Reason)
    .Do(PostAsync)
    .Do(NoteAsync);
}
