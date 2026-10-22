namespace DemoApp;

public abstract class AppFormService<TEntity, TInput>(AppDbContext db, AppService? app = null)
  : FormService<TEntity, TInput>(db)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected AppService? AppService => app;
}
