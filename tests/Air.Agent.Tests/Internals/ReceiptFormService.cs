namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestReceipt>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetElements()
  {
    Elements.Add(r => r.Title).Required();
    Elements.Add(r => r.Amount);
    Elements.Add(r => r.CategoryId).AsReference<CategoryGridService>();
    Elements.AddChild<ReceiptLineGridService>().Key("lines").SumTo("amount", "amount");
  }
}
