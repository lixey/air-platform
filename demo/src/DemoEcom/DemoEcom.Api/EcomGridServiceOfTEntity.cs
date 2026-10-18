using Air.App;
using Air.App.Grid;
using Air.Data.Entities;
using DemoEcom.Data;

namespace DemoEcom;

public abstract class EcomGridService<TEntity>(EcomDbContext db, PlatformAppService? app = null)
  : GridService<TEntity>(db, app)
  where TEntity : class, IEntity, new()
{
  protected EcomDbContext Db => db;
  protected PlatformAppService? EcomApp => app;
}
