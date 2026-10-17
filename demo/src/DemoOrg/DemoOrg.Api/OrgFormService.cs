using Air.App.Form;
using Air.Data.Entities;
using DemoOrg.Data;

namespace DemoOrg;

public abstract class OrgFormService<TEntity, TInput>(OrgDbContext db, OrgService? org = null)
  : FormService<TEntity, TInput>(db)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected OrgService? OrgService => org;
}
