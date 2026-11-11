namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyAgentCategoryGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestCategory>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyCategories;
  protected override void SetNav(Nav nav) => nav.AddCreate<ReadOnlyAgentCategoryFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<ReadOnlyAgentCategoryFormService>();
    columns.Add(c => c.Name);
  }
}
