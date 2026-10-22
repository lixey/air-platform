namespace DemoOrg;

public abstract class OrgFormService<TEntity, TInput>(OrgDbContext db, PlatformAppService? app = null)
  : FormService<TEntity, TInput>(db, app)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected OrgDbContext Db => db;
  protected PlatformAppService? OrgService => app;
}
