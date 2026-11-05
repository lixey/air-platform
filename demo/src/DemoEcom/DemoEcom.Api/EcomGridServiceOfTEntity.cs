namespace DemoEcom;

public abstract class EcomGridService<TEntity>(EcomDbContext db, PlatformAppService? app = null)
  : EcomGridService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
