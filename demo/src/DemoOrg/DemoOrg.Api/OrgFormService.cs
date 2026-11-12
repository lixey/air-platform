namespace DemoOrg;

public abstract class OrgFormService<TEntity>(OrgDbContext db, OrgService? org = null)
  : FormService<TEntity>(db, org)
  where TEntity : class, IEntity, new()
{
  protected OrgDbContext Db => db;
  protected OrgService? OrgService => org;
}
