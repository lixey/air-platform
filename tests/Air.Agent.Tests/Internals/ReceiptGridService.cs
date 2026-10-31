namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestReceipt>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetNav(Nav nav) => nav.AddCreate<ReceiptFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<ReceiptFormService>();
    columns.Add(r => r.Title);
    columns.Add(r => r.Amount);
  }
}
