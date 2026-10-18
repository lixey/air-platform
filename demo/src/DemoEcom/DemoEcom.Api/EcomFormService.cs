using Air.App;
using Air.App.Form;
using Air.Data.Entities;
using DemoEcom.Data;

namespace DemoEcom;

public abstract class EcomFormService<TEntity, TInput>(EcomDbContext db, PlatformAppService? app = null)
  : FormService<TEntity, TInput>(db, app)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected EcomDbContext Db => db;
  protected PlatformAppService? EcomApp => app;
}
