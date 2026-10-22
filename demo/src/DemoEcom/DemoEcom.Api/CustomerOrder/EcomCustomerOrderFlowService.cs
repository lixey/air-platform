namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long New = EcomData.DocStatus.CustomerOrderNewId;
  private const long InProgress = EcomData.DocStatus.CustomerOrderInProgressId;
  private const long Confirmed = EcomData.DocStatus.CustomerOrderConfirmedId;
  private const long Paused = EcomData.DocStatus.CustomerOrderPausedId;
  private const long Completed = EcomData.DocStatus.CustomerOrderCompletedId;
  private const long Canceled = EcomData.DocStatus.CustomerOrderCanceledId;

  protected override long Type => EcomData.DocType.CustomerOrderId;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetFlow(Flow<EcomDoc> flow)
  {
    base.SetFlow(flow);
    flow.Status(New, L["New"], UiColor.Sky);
    flow.Status(InProgress, L["In progress"], UiColor.Amber);
    flow.Status(Confirmed, L["Confirmed"], UiColor.Blue);
    flow.Status(Paused, L["Paused"], UiColor.Amber);
    flow.Status(Completed, L["Completed"], UiColor.Green);
    flow.Status(Canceled, L["Canceled"], UiColor.Red);
    flow.Initial(New);
  }

  private Expression<Func<EcomDoc, bool>> HasLines => order => ((EcomDbContext)Db).EcomDocLine.Any(line => line.DocId == order.Id);

  private string NoLines => L["An order without lines takes no status."];

  public Transition<EcomDoc> Reopen => From(InProgress, Confirmed, Paused).To(New).Do(PostAsync);

  public Transition<EcomDoc> Start => From(New, Confirmed, Paused).To(InProgress).Do(PostAsync);

  public Transition<EcomDoc> Confirm => From(New, InProgress, Paused).To(Confirmed).AvailableWhen(HasLines, NoLines).Do(PostAsync);

  public Transition<EcomDoc> Pause => From(New, InProgress, Confirmed).To(Paused).AvailableWhen(HasLines, NoLines).Do(PostAsync);

  public Transition<EcomDoc> Complete => From(New, InProgress, Confirmed, Paused).To(Completed).AvailableWhen(HasLines, NoLines).Do(PostAsync);

  public Transition<EcomDoc> Cancel => From(New, InProgress, Confirmed, Paused).To(Canceled)
    .Confirm(Texts.CancelConfirm)
    .Ask<EcomDocCancelInput>(input => EcomDocActions.AskReason(input, r => r.Reason), note: r => r.Reason)
    .Do(PostAsync)
    .Do(NoteAsync);
}
