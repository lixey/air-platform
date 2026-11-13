namespace Platform.Agent.Tests.Internals;

internal sealed class LeafTwoOrderFormService(TestLeafTwoDbContext db, PlatformAppService app)
  : FormService<TestLeafTwoOrder>(db, app)
{
  public override Feature Feature => AgentTestModule.LeafTwoOrders;
  protected override void SetElements()
  {
    Elements.Add(x => x.ReferenceNumber).Required();
    Elements.Add(x => x.Quantity);
    Elements.Add(x => x.CategoryId).AsReference<CategoryGridService>();
  }

  protected override void SetHooks()
  {
    Hooks.BeforeSave((ctx, _) =>
    {
      if (ctx.Entity.Quantity < 0)
      {
        ctx.AddError("order.negative_qty", "Order quantity cannot be negative.", "quantity");
      }

      return Task.CompletedTask;
    });
  }
}
