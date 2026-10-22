namespace DemoApp;

public abstract class AppGridService<TEntity, TRow>(AppDbContext db, AppService? app = null)
  : GridService<TEntity, TRow>(db)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected AppService? AppService => app;
}
