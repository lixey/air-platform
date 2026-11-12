namespace Platform.App.Tests.Internals;

internal sealed class TestSalesDocFormService(AppTestDbContext db, PlatformAppService? app = null)
  : TestDocFormServiceBase(db, app)
{
  public override Feature Feature => TestAppModule.SalesDocs;

  protected override long DocStatusKind => 10;
}
