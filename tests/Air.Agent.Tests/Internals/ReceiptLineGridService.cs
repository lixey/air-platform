namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptLineGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestReceiptLine>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetParent() => Parent.Bind<ReceiptFormService>(l => l.ReceiptId);
  protected override void SetNav() => Nav.AddCreate<ReceiptLineFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<ReceiptLineFormService>();
    Columns.Add(l => l.Name);
    Columns.Add(l => l.Amount);
  }
}
