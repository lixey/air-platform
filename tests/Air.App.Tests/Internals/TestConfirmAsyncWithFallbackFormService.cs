namespace Platform.App.Tests.Internals;

internal sealed class TestConfirmAsyncWithFallbackFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetElements() =>
    Elements.Add(e => e.Number);

  protected override void SetActions() =>
    Actions.Add("Approve")
      .AllowAction()
      .Label("Approve")
      .ConfirmAsync(
        async (inv, ct) =>
        {
          var lineCount = await db.InvoiceLines.CountAsync(l => l.InvoiceId == inv.Id, ct);
          return lineCount > 0 ? $"Approve {lineCount} lines?" : null;
        },
        gridConfirm: "Approve selected invoices?")
      .Do((_, _) => Task.CompletedTask);
}
