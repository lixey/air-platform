namespace Platform.Agent.Tests.Internals;

internal sealed class LeafOneItemGridService(TestLeafOneDbContext db, PlatformAppService app)
  : GridService<TestLeafOneItem>(db, app)
{
  public override Feature Feature => AgentTestModule.LeafOneItems;
  protected override void SetNav() => Nav.AddCreate<LeafOneItemFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<LeafOneItemFormService>();
    Columns.Add(x => x.Code);
  }
}
