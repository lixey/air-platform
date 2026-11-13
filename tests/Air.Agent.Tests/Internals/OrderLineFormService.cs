namespace Platform.Agent.Tests.Internals;

internal sealed class OrderLineFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestOrderLine>(db, app)
{
  protected override bool IsReadOnly =>
    base.IsReadOnly || (ParentRecord<AgentTestOrder>() is { } doc ? doc.OrderStatus is not { IsEditable: true } : !IsNew);

  public override Feature Feature => AgentTestModule.Orders;

  protected override void SetParent(Parent parent) => parent.Bind<OrderFormService>(l => l.OrderId);

  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.ItemName).Required();
    elements.Add(l => l.Qty);
    elements.Add(l => l.Price);
    elements.Add(l => l.DiscountPercent);
    elements.Add(l => l.Amount).ServerOwned();
    elements.Add(l => l.Note).AsTextarea();
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad((c, _) =>
    {
      if (c.IsNew && ParentRecord<AgentTestOrder>() is { } order && c.Entity.DiscountPercent == 0m)
      {
        c.Entity.DiscountPercent = order.DefaultDiscountPercent;
      }

      return Task.CompletedTask;
    });
    hooks.Compute(c =>
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2));
    hooks.BeforeSave((c, _) =>
    {
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      return Task.CompletedTask;
    });
  }
}
