using Air.App;
using Air.App.Form;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgFormService<TEntity>(OrgDbContext db, PlatformAppService? app = null)
  : FormService<TEntity>(db, app)
  where TEntity : class, IEntity, new()
{
  protected OrgDbContext Db => db;
  protected PlatformAppService? OrgService => app;
}
