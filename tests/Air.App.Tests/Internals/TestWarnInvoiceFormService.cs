namespace Platform.App.Tests.Internals;

internal sealed class TestWarnInvoiceFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.WarnInvoices;

  protected override void SetElements()
  {
    Elements.Add(x => x.Number).Required();
    Elements.Add(x => x.AuditedTotal);
  }

  protected override void SetRules()
  {
    Rules.For(x => x.AuditedTotal)
      .Must(total => total <= 1000m)
      .WithMessage("Total exceeds 1000 — check manager approval.")
      .AsWarning();

    Rules.For(x => x.Number)
      .Custom((num, ctx) =>
      {
        if (num.StartsWith("DRAFT-", StringComparison.OrdinalIgnoreCase))
        {
          ctx.Warn("Draft prefix used on invoice number.", code: "draft_prefix");
        }
      });
  }

  protected override void SetHooks()
  {
    Hooks.Compute(c => c.Items["computedNumber"] = c.Entity.Number);
    Hooks.BeforeSave(c =>
    {
      if (c.Entity.AuditedTotal == 0m)
      {
        c.Warn("Zero total invoice saved.", path: "auditedTotal", code: "zero_total");
        c.Items["zeroFlagged"] = true;
      }
    });
    Hooks.AfterSave(c =>
    {
      if (c.Items.TryGetValue("zeroFlagged", out var flag) && flag is true &&
          c.Items.TryGetValue("computedNumber", out var num) && num is string computedNum)
      {
        c.Entity.InternalMemo = $"zero:{computedNum}";
      }
    });
  }
}
