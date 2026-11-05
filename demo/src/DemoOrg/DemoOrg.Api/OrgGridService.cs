namespace DemoOrg;

public abstract class OrgGridService<TEntity, TRow>(OrgDbContext db, PlatformAppService? app = null)
  : GridService<TEntity, TRow>(db, app)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected OrgDbContext Db => db;
  protected PlatformAppService? OrgService => app;
}
