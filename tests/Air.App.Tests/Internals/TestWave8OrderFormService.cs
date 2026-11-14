namespace Platform.App.Tests.Internals;

internal sealed class TestWave8OrderFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Wave8Orders;

  protected override void SetQuery() => Query.From(db.Invoices).Where(i => !i.IsHidden);

  protected override void SetElements()
  {
    Elements.UseStatusOf(new TestWave8OrderFlowService(db));
    Elements.Add(i => i.Number).Required();
    Elements.Add(i => i.Status);
    Elements.Add(i => i.CachedLineTotal);
    Elements.Add(i => i.AuditedTotal).Disabled();
    Elements.Add(i => i.ApiSecret);
    Elements.AddChild<TestWave8OrderLineGridService>().Key("lines").SumTo("cachedLineTotal", "amount");
  }

  protected override void SetRules()
  {
    Rules.For(i => i.Number)
      .Must(n => n != "BLOCKED")
      .WithMessage("Blocked order number.");

    Rules.For(i => i.CachedLineTotal)
      .Must(t => t <= 1_000m)
      .WithMessage("Large order total requires review.")
      .AsWarning();
  }

  protected override async Task RecalculateAsync(TestInvoice entity, CancellationToken ct)
  {
    entity.PrintedCount++;
    entity.AuditedTotal = await db.InvoiceLines
      .Where(l => l.InvoiceId == entity.Id)
      .SumAsync(l => l.Amount, ct);
  }

  protected override void SetActions()
  {
    Actions.Add("StampAgent")
      .Allow(TestAppModule.ApproveInvoice)
      .AllowAgent()
      .When(i => i.Status == 1, "Only draft orders can be stamped.")
      .Ask<TestAssignTagInput>(els =>
      {
        els.Add(x => x.TagId).AsReference<TestTagGridService>().Required();
        els.Add(x => x.Note).Required();
      })
      .Default((inv, _) => Task.FromResult(new TestAssignTagInput { Note = $"Stamp:{inv?.Number}" }))
      .Do(async (ctx, inv, input, ct) =>
      {
        var tag = await db.Tags.FirstAsync(t => t.Id == input.TagId, ct);
        inv.Number = $"{inv.Number}-{tag.Name}-{input.Note}";
        ctx.ShowNotice("Stamped", [new MessageDto { Detail = $"Applied {tag.Name}" }]);
      });

    Actions.Add("UiOnlyStamp")
      .AllowEdit()
      .Do((_, inv, _) =>
      {
        inv.InternalMemo = "UI-only";
        return Task.CompletedTask;
      });
  }
}
