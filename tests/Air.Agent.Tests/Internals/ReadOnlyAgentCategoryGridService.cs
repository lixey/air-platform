namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyAgentCategoryGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestCategory>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyCategories;
  protected override void SetNav() => Nav.AddCreate<ReadOnlyAgentCategoryFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<ReadOnlyAgentCategoryFormService>();
    Columns.Add(c => c.Name);
  }

  protected override void SetActions()
  {
    Actions.Add("AuditPing")
      .AllowView()
      .WithoutSelection()
      .WithoutTransaction()
      .AllowAgent()
      .Do((ctx, _) =>
      {
        ctx.ShowNotice("AuditReady", [new MessageDto { Detail = "Checked" }]);
        return Task.CompletedTask;
      });
  }
}
