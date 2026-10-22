namespace DemoEcom;

public abstract class EcomGridService<TEntity, TRow>(EcomDbContext db, PlatformAppService? app = null)
  : GridService<TEntity, TRow>(db, app)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected EcomDbContext Db => db;
  protected PlatformAppService? EcomApp => app;
}
