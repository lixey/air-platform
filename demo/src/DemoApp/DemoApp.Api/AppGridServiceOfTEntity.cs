namespace DemoApp;

public abstract class AppGridService<TEntity>(AppDbContext db, AppService? app = null)
  : AppGridService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
