namespace Platform.App.Tests.Internals;

internal sealed class TestPurchaseDocFormService(AppTestDbContext db, PlatformAppService? app = null)
  : TestDocFormServiceBase(db, app)
{
  public override Feature Feature => TestAppModule.PurchaseDocs;

  protected override long DocStatusKind => 20;
}
