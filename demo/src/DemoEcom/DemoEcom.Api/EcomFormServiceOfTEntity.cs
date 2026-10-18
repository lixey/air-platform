using Air.App;
using Air.App.Form;
using Air.Data.Entities;
using DemoEcom.Data;

namespace DemoEcom;

public abstract class EcomFormService<TEntity>(EcomDbContext db, PlatformAppService? app = null)
  : FormService<TEntity>(db, app)
  where TEntity : class, IEntity, new()
{
  protected EcomDbContext Db => db;
  protected PlatformAppService? EcomApp => app;
}
