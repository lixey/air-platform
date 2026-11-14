namespace Platform.App.Tests.Internals;

internal sealed class TestWave8OrderLineFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Wave8Orders;

  protected override void SetParent() => Parent.Bind<TestWave8OrderFormService>(l => l.InvoiceId);

  protected override void SetElements()
  {
    Elements.Add(l => l.Title).Required();
    Elements.Add(l => l.Amount);
    Elements.Add(l => l.TagId).AsReference<TestTagGridService>();
  }

  protected override void SetRules()
  {
    Rules.For(l => l.Amount)
      .Must(a => a >= 0m)
      .WithMessage("Line amount cannot be negative.");

    Rules.For(l => l.Amount)
      .Must(a => a <= 500m)
      .WithMessage("High single-line amount.")
      .AsWarning();
  }
}
