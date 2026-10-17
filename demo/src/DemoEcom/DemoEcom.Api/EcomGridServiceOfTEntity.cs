namespace DemoEcom;

public abstract class EcomGridService<TEntity>(EcomDbContext db, EcomService? ecom = null)
  : GridService<TEntity>(db)
  where TEntity : class, IEntity, new()
{
  protected EcomDbContext Db => db;
  protected EcomService? EcomApp => ecom;
}


