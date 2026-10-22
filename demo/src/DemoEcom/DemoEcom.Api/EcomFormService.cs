namespace DemoEcom;

public abstract class EcomFormService<TEntity, TInput>(EcomDbContext db, PlatformAppService? app = null)
  : FormService<TEntity, TInput>(db, app)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected EcomDbContext Db => db;
  protected PlatformAppService? EcomApp => app;
}
