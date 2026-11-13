namespace Platform.Agent.Tests.Internals;

internal sealed class OrderGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestOrder>(db, app)
{
  public override Feature Feature => AgentTestModule.Orders;
  protected override void SetNav(Nav nav) => nav.AddCreate<OrderFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<OrderFormService>();
    columns.Add(o => o.Number);
    columns.Add(o => o.Amount).Sum();
  }
}
