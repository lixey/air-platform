namespace DemoEcom;

public abstract class EcomFormService<TEntity, TInput>(EcomDbContext db, EcomService? ecom = null)
  : FormService<TEntity, TInput>(db)
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected EcomDbContext Db => db;
  protected EcomService? EcomApp => ecom;
}
