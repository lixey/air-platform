namespace Platform.Agent.Tests.Internals;

internal sealed class ReceiptFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestReceipt>(db, app)
{
  public override Feature Feature => AgentTestModule.Receipts;
  protected override void SetElements()
  {
    Elements.Add(r => r.Title).Required();
    Elements.Add(r => r.Amount);
    Elements.Add(r => r.CategoryId).AsReference<CategoryGridService>();
    Elements.Add(r => r.ReadOnlyCategoryId).AsReference<ReadOnlyAgentCategoryGridService>();
    Elements.Add(r => r.SecretToken);
    Elements.AddChild<ReceiptLineGridService>().Key("lines").SumTo("amount", "amount");
  }

  protected override void SetRules()
  {
    Rules.For(r => r.Title)
      .Must(t => t != "FORBIDDEN_RECEIPT")
      .WithMessage("Forbidden receipt title.");

    Rules.For(r => r.Amount)
      .Must(a => a <= 50_000m)
      .WithMessage("Receipt above 50,000 requires CFO review.")
      .AsWarning();
  }

  protected override Task RecalculateAsync(AgentTestReceipt entity, CancellationToken ct)
  {
    entity.RecalcCount++;
    return Task.CompletedTask;
  }
}
