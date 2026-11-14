namespace Platform.App.Tests.Internals;

internal sealed class TestWave8OrderGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Wave8Orders;

  protected override void SetNav() => Nav.AddCreate<TestWave8OrderFormService>();

  protected override void SetColumns()
  {
    Columns.UseForm<TestWave8OrderFormService>();
    Columns.UseStatusOf(new TestWave8OrderFlowService(db));
    Columns.Add(i => i.Number);
    Columns.Add(i => i.Status);
    Columns.Add(i => i.CachedLineTotal);
    Columns.Add(i => i.AuditedTotal);
  }

  protected override void SetActions()
  {
    Actions.UseActionsOf<TestWave8OrderFormService>();

    Actions.Add("GlobalAgentPing")
      .AllowView()
      .WithoutSelection()
      .WithoutTransaction()
      .AllowAgent()
      .Do((ctx, _) =>
      {
        ctx.ShowNotice("AgentPong", [new MessageDto { Detail = "Ready" }]);
        return Task.CompletedTask;
      });

    Actions.Add("UiOnlyBulk")
      .AllowEdit()
      .Do((ctx, _) =>
      {
        foreach (var row in ctx.Records)
        {
          row.InternalMemo = "Bulk";
        }

        return Task.CompletedTask;
      });
  }
}
