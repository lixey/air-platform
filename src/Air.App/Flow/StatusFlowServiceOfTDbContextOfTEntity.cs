namespace Platform.App.Flow;

public abstract class StatusFlowService<TDbContext, TEntity>(TDbContext db, PlatformAppService? app = null)
  : StatusFlowService<TEntity>(db, app)
  where TDbContext : PlatformDbContext
  where TEntity : class, IEntity, new()
{
  protected new TDbContext Db => db;
}
