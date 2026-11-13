namespace Platform.Agent.Tests.Internals;

internal sealed class OrderLineGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestOrderLine>(db, app)
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<AgentTestOrder>() is not { OrderStatus.IsEditable: true };

  public override Feature Feature => AgentTestModule.Orders;

  protected override void SetParent(Parent parent) => parent.Bind<OrderFormService>(l => l.OrderId);
  protected override void SetNav(Nav nav) => nav.AddCreate<OrderLineFormService>();

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<OrderLineFormService>();
    columns.Add(l => l.ItemName);
    columns.Add(l => l.Qty);
    columns.Add(l => l.Price);
    columns.Add(l => l.DiscountPercent);
    columns.Add(l => l.Amount).Sum();
  }

  protected override void SetHooks(Hooks hooks) => hooks.AllowMultiDelete();
}
