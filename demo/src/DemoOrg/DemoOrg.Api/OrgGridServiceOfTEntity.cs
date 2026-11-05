namespace DemoOrg;

public abstract class OrgGridService<TEntity>(OrgDbContext db, PlatformAppService? app = null)
  : OrgGridService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
