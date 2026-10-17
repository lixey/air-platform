using Air.App.Form;
using Air.Data.Entities;
using DemoApp.Data;

namespace DemoApp;

public abstract class AppFormService<TEntity>(AppDbContext db, AppService? app = null)
  : FormService<TEntity>(db)
  where TEntity : class, IEntity, new()
{
  protected AppService? AppService => app;
}
