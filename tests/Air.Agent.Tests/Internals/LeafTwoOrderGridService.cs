namespace Platform.Agent.Tests.Internals;

internal sealed class LeafTwoOrderGridService(TestLeafTwoDbContext db, PlatformAppService app)
  : GridService<TestLeafTwoOrder>(db, app)
{
  public override Feature Feature => AgentTestModule.LeafTwoOrders;
  protected override void SetNav(Nav nav) => nav.AddCreate<LeafTwoOrderFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<LeafTwoOrderFormService>();
    columns.Add(x => x.ReferenceNumber);
    columns.Add(x => x.Quantity);
  }
}
