namespace DemoOrg;

public abstract class OrgGridService<TEntity>(OrgDbContext db, OrgService? org = null)
  : OrgGridService<TEntity, TEntity>(db, org)
  where TEntity : class, IEntity, new();
