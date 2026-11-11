namespace Platform.App.Tests.Internals;

internal sealed class TestOrderMasterGridService(
  AppTestDbContext db,
  PlatformAppService? app = null,
  List<string>? events = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetQuery(Query query) => query.Where(i => !i.IsHidden);

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestOrderMasterFormService>();
    columns.UseStatusOf(new TestOrderFlowService(db, events));
    columns.Add(i => i.Number).Editable();
    columns.Add(i => i.Status);
    columns.Add(i => i.AuditedTotal);
    columns.Add(i => i.PrintedCount);
  }

  protected override void SetActions(Actions actions)
  {
    actions.UseActionsOf<TestOrderMasterFormService>();

    actions.Add("BulkReset")
      .AllowEdit()
      .Do((ctx, _) =>
      {
        foreach (var row in ctx.Records)
        {
          row.PrintedCount = 0;
        }

        return Task.CompletedTask;
      });

    actions.Add("GlobalPing")
      .AllowView()
      .WithoutSelection()
      .WithoutTransaction()
      .Do((ctx, _) =>
      {
        ctx.ShowNotice("Pong", [new MessageDto { Detail = "Alive" }]);
        return Task.CompletedTask;
      });
  }
}
