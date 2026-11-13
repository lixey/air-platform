namespace Platform.Agent.Tests.Internals;

internal sealed class OrderGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestOrder>(db, app)
{
  public override Feature Feature => AgentTestModule.Orders;
  protected override void SetNav() => Nav.AddCreate<OrderFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<OrderFormService>();
    Columns.Add(o => o.Number);
    Columns.Add(o => o.Amount).Sum();
  }
}
