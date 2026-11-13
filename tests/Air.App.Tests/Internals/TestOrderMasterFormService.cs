namespace Platform.App.Tests.Internals;

internal sealed class TestOrderMasterFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetQuery() => Query.From(db.Invoices).Where(i => !i.IsHidden);

  protected override void SetElements()
  {
    Elements.UseStatusOf(new TestOrderFlowService(db));
    Elements.Add(i => i.Number).Required();
    Elements.Add(i => i.Status);
    Elements.Add(i => i.AuditedTotal).Disabled();
    Elements.AddChild<TestOrderDetailGridService>();
  }

  protected override async Task RecalculateAsync(TestInvoice entity, CancellationToken ct)
  {
    entity.AuditedTotal = await db.InvoiceLines
      .Where(l => l.InvoiceId == entity.Id)
      .SumAsync(l => l.Amount, ct);
  }

  protected override void SetActions()
  {
    Actions.Add("StampNote")
      .Allow(TestAppModule.ApproveInvoice)
      .When(i => i.Status == 1, "Only open orders can be stamped")
      .Ask<TestAssignTagInput>(els =>
      {
        els.Add(x => x.TagId).AsReference<TestTagGridService>().Required();
        els.Add(x => x.Note).Required();
      })
      .Default((inv, _) => Task.FromResult(new TestAssignTagInput { Note = $"Ref:{inv?.Number}" }))
      .Do(async (ctx, inv, input, ct) =>
      {
        var tag = await db.Tags.FirstAsync(t => t.Id == input.TagId, ct);
        inv.Number = $"{inv.Number}-{tag.Name}-{input.Note}";
        ctx.ShowNotice("Stamped", [new MessageDto { Detail = $"Applied {tag.Name}" }]);
      });
  }
}
