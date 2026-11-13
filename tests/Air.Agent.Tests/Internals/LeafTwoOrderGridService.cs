namespace Platform.Agent.Tests.Internals;

internal sealed class LeafTwoOrderGridService(TestLeafTwoDbContext db, PlatformAppService app)
  : GridService<TestLeafTwoOrder>(db, app)
{
  public override Feature Feature => AgentTestModule.LeafTwoOrders;
  protected override void SetNav() => Nav.AddCreate<LeafTwoOrderFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<LeafTwoOrderFormService>();
    Columns.Add(x => x.ReferenceNumber);
    Columns.Add(x => x.Quantity);
  }
}
