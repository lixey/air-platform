using Air.App;
using Air.App.Grid;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgGridService<TEntity>(OrgDbContext db, PlatformAppService? app = null)
  : GridService<TEntity>(db, app)
  where TEntity : class, IEntity, new()
{
  protected OrgDbContext Db => db;
  protected PlatformAppService? OrgService => app;
}
