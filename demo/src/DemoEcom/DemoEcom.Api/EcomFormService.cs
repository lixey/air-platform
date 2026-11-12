namespace DemoEcom;

public abstract class EcomFormService<TEntity>(EcomDbContext db, EcomService? ecom = null)
  : FormService<TEntity>(db, ecom)
  where TEntity : class, IEntity, new()
{
  protected EcomDbContext Db => db;
  protected EcomService? EcomApp => ecom;
}
