namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptLineGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestReceiptLine>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetParent(Parent parent) => parent.Bind<ReceiptFormService>(l => l.ReceiptId);
  protected override void SetNav(Nav nav) => nav.AddCreate<ReceiptLineFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<ReceiptLineFormService>();
    columns.Add(l => l.Name);
    columns.Add(l => l.Amount);
  }
}
