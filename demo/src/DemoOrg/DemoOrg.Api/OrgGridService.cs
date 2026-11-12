namespace DemoOrg;

public abstract class OrgGridService<TEntity, TRow>(OrgDbContext db, OrgService? org = null)
  : GridService<TEntity, TRow>(db, org)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected OrgDbContext Db => db;
  protected OrgService? OrgService => org;
}
