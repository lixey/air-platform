namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestReceipt>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetElements(Elements elements)
  {
    elements.Add(r => r.Title).Required();
    elements.Add(r => r.Amount);
    elements.Add(r => r.CategoryId).AsReference<CategoryGridService>();
    elements.AddChild<ReceiptLineGridService>();
  }
}
