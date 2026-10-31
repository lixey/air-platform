namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptLineFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestReceiptLine>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetParent(Parent parent) => parent.Bind<ReceiptFormService>(l => l.ReceiptId);
  protected override void SetElements(Elements elements)
  {
    elements.Add(l => l.Name).Required();
    elements.Add(l => l.Amount);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeSave((ctx, _) =>
    {
      if (ctx.Entity.Amount < 0)
      {
        ctx.AddError("line.negative", "Line amount cannot be negative.", "amount");
      }

      return Task.CompletedTask;
    });
  }
}
