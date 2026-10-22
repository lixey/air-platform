namespace DemoOrg.Product;

public sealed class OrgProductFileGridService(OrgDbContext db, AppService? app = null)
  : AppFileGridService<OrgProductFile>(db, app)
{
  public override Feature Feature => Org.Product;

  protected override void SetParent(Parent parent) => parent.Bind<OrgProductFormService>(e => e.ProductId);
}
