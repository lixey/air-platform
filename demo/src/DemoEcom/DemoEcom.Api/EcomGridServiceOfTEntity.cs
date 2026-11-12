namespace DemoEcom;

public abstract class EcomGridService<TEntity>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<TEntity, TEntity>(db, ecom)
  where TEntity : class, IEntity, new();
