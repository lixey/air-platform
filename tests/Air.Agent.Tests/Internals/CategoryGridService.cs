namespace Platform.Agent.Tests.Internals;

internal sealed class CategoryGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestCategory>(db, app)
{
  public override Feature Feature => AgentTestModule.Categories;
  protected override void SetNav(Nav nav) => nav.AddCreate<CategoryFormService>();
  protected override void SetColumns(Columns columns) => columns.Add(c => c.Name);
}
