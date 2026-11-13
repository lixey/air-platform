namespace Platform.Agent.Tests.Internals;

internal sealed class OrderFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestOrder>(db, app)
{
  private AgentTestOrderStatus? Status
  {
    get => ExecutionItems.TryGetValue(nameof(Status), out var v) && v is AgentTestOrderStatus s ? s : CurrentEntity?.OrderStatus;
    set => ExecutionItems[nameof(Status)] = value;
  }

  protected override bool IsReadOnly => base.IsReadOnly || Status is { IsFinal: true };

  public override Feature Feature => AgentTestModule.Orders;

  protected override void SetQuery(Query query) =>
    query.From(db.Orders.Include(o => o.OrderStatus));

  protected override async Task SetElementsAsync(Elements elements, CancellationToken ct)
  {
    var statuses = await db.OrderStatuses.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
    Status = IsNew
      ? statuses.FirstOrDefault()
      : CurrentEntity?.OrderStatus ?? statuses.FirstOrDefault(s => s.Id == CurrentEntity?.OrderStatusId);

    elements.Disable(Status is not { IsEditable: true });
    elements.Add(o => o.Number).AsInfo();
    elements.Add(o => o.DefaultDiscountPercent);
    elements.Add(o => o.Note);
    elements.AddChild<OrderLineGridService, AgentTestOrderLine>()
      .Key("lines")
      .SumTo(o => o.Amount, l => l.Amount);
  }

  protected override void SetHooks(Hooks hooks) =>
    hooks.BeforeSave(async (c, ct) =>
    {
      if (c.IsNew)
      {
        var initial = await db.OrderStatuses.AsNoTracking().OrderBy(s => s.Id).FirstAsync(ct);
        c.Entity.OrderStatusId = initial.Id;
        c.Entity.Number = $"ORD-{Guid.NewGuid().ToString("N")[..6]}";
      }
    });
}
