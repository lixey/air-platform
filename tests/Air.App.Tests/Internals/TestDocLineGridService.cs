namespace Platform.App.Tests.Internals;

internal sealed class TestDocLineGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.DocLines;

  protected override void SetParent() =>
    Parent.Bind<TestDocFormServiceBase>(l => l.InvoiceId);

  protected override void SetNav()
  {
    Nav.AddCreate<TestDocLineFormService>();
    Nav.AddPicker<TestTagGridService>(PickTag);
  }

  protected override void SetColumns()
  {
    Columns.UseForm<TestDocLineFormService>();
    Columns.Add(l => l.Title).Editable();
    Columns.Add(l => l.Amount).Editable();
    Columns.Add(l => l.TierCode).Editable();
  }

  [RequireAction(Right.Edit)]
  public Task<GridActionDto> PickTag(GridPickRequest request, CancellationToken ct = default) =>
    AttachPickedAsync<TestTag>(
      request,
      (picked, line) =>
      {
        line.TagId = picked.Id;
        line.Title = picked.Name;
        line.Amount = picked.DefaultPrice;
        line.TierCode = 1;
      },
      ct);
}
