namespace Platform.Agent.Tests.Internals;

internal sealed class OrderLineGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestOrderLine>(db, app)
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<AgentTestOrder>() is not { OrderStatus.IsEditable: true };

  public override Feature Feature => AgentTestModule.Orders;

  protected override void SetParent() => Parent.Bind<OrderFormService>(l => l.OrderId);
  protected override void SetNav() => Nav.AddCreate<OrderLineFormService>();

  protected override void SetColumns()
  {
    Columns.UseForm<OrderLineFormService>();
    Columns.Add(l => l.ItemName);
    Columns.Add(l => l.Qty);
    Columns.Add(l => l.Price);
    Columns.Add(l => l.DiscountPercent);
    Columns.Add(l => l.Amount).Sum();
  }

  protected override void SetHooks() => Hooks.AllowMultiDelete();
}
