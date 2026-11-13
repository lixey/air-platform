namespace Platform.Agent.Tests.Internals;

internal sealed class OrderLineFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestOrderLine>(db, app)
{
  protected override bool IsReadOnly =>
    base.IsReadOnly || (ParentRecord<AgentTestOrder>() is { } doc ? doc.OrderStatus is not { IsEditable: true } : !IsNew);

  public override Feature Feature => AgentTestModule.Orders;

  protected override void SetParent() => Parent.Bind<OrderFormService>(l => l.OrderId);

  protected override void SetElements()
  {
    Elements.Add(l => l.ItemName).Required();
    Elements.Add(l => l.Qty);
    Elements.Add(l => l.Price);
    Elements.Add(l => l.DiscountPercent);
    Elements.Add(l => l.Amount).ServerOwned();
    Elements.Add(l => l.Note).AsTextarea();
  }

  protected override void SetHooks()
  {
    Hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew && ParentRecord<AgentTestOrder>() is { } order && c.Entity.DiscountPercent == 0m)
      {
        c.Entity.DiscountPercent = order.DefaultDiscountPercent;
      }

      return Task.CompletedTask;
    });
    Hooks.Compute(c =>
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2));
    Hooks.BeforeSave((c, _) =>
    {
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      return Task.CompletedTask;
    });
  }
}
