using Air.App.Grid;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgGridService<TEntity, TRow>(OrgDbContext db, OrgService? org = null)
  : GridService<TEntity, TRow>(db)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected OrgService? OrgService => org;
}
