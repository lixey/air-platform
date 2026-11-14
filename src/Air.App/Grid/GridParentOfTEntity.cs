using Platform.App.Internals;

namespace Platform.App.Grid;

public sealed class GridParent<TEntity>
  where TEntity : class
{
  internal Func<IQueryable<TEntity>, long, IQueryable<TEntity>>? FilterFunc { get; private set; }
  public Type? BoundParentServiceType { get; private set; }
  public Type? BoundParentEntityType => ParentScopeValidator.GetFormEntityType(BoundParentServiceType);
  public string? BoundParentService { get; private set; }
  public PropertyInfo? BoundProperty { get; private set; }
  public bool IsOptional { get; private set; }
  public bool HasRead { get; private set; }

  public bool MatchesService(string? parentService) =>
    ParentScopeValidator.MatchesService(parentService, BoundParentService, BoundParentServiceType);

  public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
  {
    BoundParentServiceType = typeof(TParentService);
    BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    IsOptional = optional;
    BoundProperty = ParentFilterExpressions.ExtractBoundProperty(member);

    var param = member.Parameters[0];
    FilterFunc = (q, parentId) =>
    {
      var body = Expression.Equal(member.Body, Expression.Constant(parentId));
      var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
      return q.Where(lambda);
    };
  }

  public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
  {
    BoundParentServiceType = typeof(TParentService);
    BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    IsOptional = optional;
    BoundProperty = ParentFilterExpressions.ExtractBoundProperty(member);

    var param = member.Parameters[0];
    FilterFunc = (q, parentId) =>
    {
      var body = Expression.Equal(member.Body, Expression.Constant((long?)parentId, typeof(long?)));
      var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
      return q.Where(lambda);
    };
  }

  public void Read<TParentService>()
  {
    BoundParentServiceType = typeof(TParentService);
    BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    HasRead = true;
  }

  internal void AllowDynamicParent()
  {
    HasRead = true;
  }
}
