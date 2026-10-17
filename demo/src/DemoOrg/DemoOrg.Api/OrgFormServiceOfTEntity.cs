using Air.App.Form;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgFormService<TEntity>(OrgDbContext db, OrgService? org = null)
  : FormService<TEntity>(db)
  where TEntity : class, IEntity, new()
{
  protected OrgService? OrgService => org;
}
