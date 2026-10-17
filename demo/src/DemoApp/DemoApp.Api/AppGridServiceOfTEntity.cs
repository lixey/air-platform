using Air.App.Grid;
using Air.Data.Entities;
using DemoApp.Data;

namespace DemoApp;

public abstract class AppGridService<TEntity>(AppDbContext db, AppService? app = null)
  : GridService<TEntity>(db)
  where TEntity : class, IEntity, new()
{
  protected AppService? AppService => app;
}
