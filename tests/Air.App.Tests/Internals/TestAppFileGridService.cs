namespace Platform.App.Tests.Internals;

internal sealed class TestAppFileGridService(AppTestDbContext db, PlatformAppService? app = null)
  : FileGridService<AppTestDbContext, TestAppFile>(db, app)
{
  public override Feature Feature => TestAppModule.AppFiles;

  protected override void SetParent(Parent parent) =>
    parent.Bind<TestSalesDocFormService>(f => f.InvoiceId, optional: true);
}
