namespace Platform.App.Form;

public abstract class FormService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : FormService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
