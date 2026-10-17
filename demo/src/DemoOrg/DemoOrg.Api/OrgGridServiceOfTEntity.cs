using Air.App.Grid;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgGridService<TEntity>(OrgDbContext db, OrgService? org = null)
  : GridService<TEntity>(db)
  where TEntity : class, IEntity, new()
{
  protected OrgService? OrgService => org;
}
