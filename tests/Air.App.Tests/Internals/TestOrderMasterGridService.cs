namespace Platform.App.Tests.Internals;

internal sealed class TestOrderMasterGridService(
  AppTestDbContext db,
  PlatformAppService? app = null,
  List<string>? events = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetQuery() => Query.Where(i => !i.IsHidden);

  protected override void SetColumns()
  {
    Columns.UseForm<TestOrderMasterFormService>();
    Columns.UseStatusOf(new TestOrderFlowService(db, events));
    Columns.Add(i => i.Number).Editable();
    Columns.Add(i => i.Status);
    Columns.Add(i => i.AuditedTotal);
    Columns.Add(i => i.PrintedCount);
  }

  protected override void SetActions()
  {
    Actions.UseActionsOf<TestOrderMasterFormService>();

    Actions.Add("BulkReset")
      .AllowEdit()
      .Do((ctx, _) =>
      {
        foreach (var row in ctx.Records)
        {
          row.PrintedCount = 0;
        }

        return Task.CompletedTask;
      });

    Actions.Add("GlobalPing")
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
