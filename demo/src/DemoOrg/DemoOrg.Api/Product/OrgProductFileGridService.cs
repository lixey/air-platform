using Air.App.Modules;
using DemoApp;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg.Product;

public sealed class OrgProductFileGridService(OrgDbContext db, AppService? app = null)
  : AppFileGridService<OrgProductFile>(db, app)
{
  public override Feature Feature => Org.Product;

  protected override void SetParent(Parent parent) => parent.Bind<OrgProductFormService>(e => e.ProductId);
}
