namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestReceipt>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetNav() => Nav.AddCreate<ReceiptFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<ReceiptFormService>();
    Columns.Add(r => r.Title);
    Columns.Add(r => r.Amount);
  }
}
