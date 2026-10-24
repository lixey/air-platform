namespace Platform.App.Grid;

public abstract class GridService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : GridService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
