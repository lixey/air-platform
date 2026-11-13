using Platform.App.Internals;

namespace Platform.App.Form;

public sealed class FormParent<TEntity>
  where TEntity : class
{
  public Type? BoundParentServiceType { get; private set; }
  public Type? BoundParentEntityType => ParentScopeValidator.GetFormEntityType(BoundParentServiceType);
  public string? BoundParentService { get; private set; }
  public PropertyInfo? BoundProperty { get; private set; }
  public bool IsOptional { get; private set; }

  public bool MatchesService(string? parentService) =>
    ParentScopeValidator.MatchesService(parentService, BoundParentService, BoundParentServiceType);

  public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
  {
    BoundParentServiceType = typeof(TParentService);
    BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    IsOptional = optional;
    BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);
  }

  public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
  {
    BoundParentServiceType = typeof(TParentService);
    BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
    IsOptional = optional;
    BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);
  }
}
