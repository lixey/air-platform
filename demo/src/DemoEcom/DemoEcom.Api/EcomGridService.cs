namespace DemoEcom;

public abstract class EcomGridService<TEntity, TRow>(EcomDbContext db, EcomService? ecom = null)
  : GridService<TEntity, TRow>(db)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected EcomDbContext Db => db;
  protected EcomService? EcomApp => ecom;
}
