using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>(PlatformDbContext db, PlatformAppService? app = null) : IGridService
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  public PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected Localizer L => Localizer.Instance.For(Feature.Module);

  protected virtual void SetUserRestrictions(UserRestrictions restrictions) { }

  public abstract Feature Feature { get; }

  protected virtual bool IsReadOnly =>
    Feature.IsReadOnly ||
    ResolveParentChildBuilder()?.IsReadOnly == true ||
    User is not { IsAuthenticated: true } ||
    !ServiceEndpointsExtensions.CanEdit(Feature, User);
  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  protected virtual void SetParent(Parent parent) { }
  protected internal string? ParentService { get; set; }
  protected internal long? ParentId { get; set; }
  protected IReadOnlyList<long> SelectedIds { get; set; } = [];

  private Parent? _cachedParent;
  private Hooks? _cachedHooks;

  protected Parent GetParent()
  {
    if (_cachedParent != null)
    {
      return _cachedParent;
    }

    var parent = new Parent();
    SetParent(parent);
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), parent.BoundParentServiceType, "Parent.Bind/Read");
    return _cachedParent = parent;
  }

  protected Hooks GetHooks()
  {
    if (_cachedHooks != null)
    {
      return _cachedHooks;
    }

    var hooks = new Hooks();
    SetHooks(hooks);
    return _cachedHooks = hooks;
  }

  public virtual bool CanView(GridRequest request, IUserContext? user) =>
    ServiceEndpointsExtensions.CanView(Feature, user);

  public bool AllowsMultiDelete => GetHooks().IsMultiDeleteAllowed;

  public virtual bool CanLookup(GridRequest request, IUserContext? user)
  {
    if (user is null or { IsAuthenticated: false })
    {
      return false;
    }

    if (CanView(request, user))
    {
      return true;
    }

    if (typeof(BaseAppFile).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthRole).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthGrant).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthAssignment).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthSession).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppPlatformChange).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppAgentMemory).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppAgentModel).IsAssignableFrom(typeof(TEntity)))
    {
      return false;
    }

    return ModuleCatalog.Instance.AllFeatures.Any(f =>
      f.ServiceType != GetType() && ServiceEndpointsExtensions.CanView(f, user));
  }

  protected IQueryable<TEntity> Reachable(bool asTracking = false)
  {
    var q = RestrictedQuery();
    return asTracking ? q.AsTracking() : q.AsNoTracking();
  }

  protected IQueryable<TEntity> Selected(bool asTracking = false) =>
    SelectedIds.Count > 0
      ? Reachable(asTracking).Where(e => SelectedIds.Contains(e.Id))
      : Reachable(asTracking).Where(_ => false);

  private object? _preloadedParent;
  private long? _preloadedParentId;

  public async Task<bool> BindParentAsync(string? parentService, long? parentId, CancellationToken ct = default)
  {
    ParentService = parentService;
    ParentId = parentId;
    if (!string.IsNullOrEmpty(parentService) || parentId is > 0)
    {
      return await EnsureParentScopeAsync(lockParent: false, ct);
    }

    return true;
  }

  protected async Task<bool> EnsureParentScopeAsync(bool lockParent, CancellationToken ct)
  {
    var parent = GetParent();
    var hasRequiredBoundParent = parent.BoundProperty != null &&
                                 !parent.IsOptional &&
                                 Feature.ServiceType != null &&
                                 Feature.ServiceType != GetType();
    var (allowed, preloaded, parentChildren) = await ParentScopeValidator.ValidateAndLoadAsync(
      db,
      PlatformApp,
      ParentId,
      ParentService,
      hasRequiredBoundParent,
      parent.MatchesService,
      parent.BoundParentService,
      parent.BoundParentServiceType,
      parent.BoundParentEntityType,
      lockParent,
      ct);
    _preloadedParent = preloaded;
    _preloadedParentId = ParentId;
    var effectiveParentService = !string.IsNullOrEmpty(ParentService)
      ? ParentService
      : parent.BoundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && parentChildren != null)
    {
      _cachedParentChildren = (effectiveParentService, ParentId, parentChildren);
    }

    return allowed;
  }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity =>
    ParentScopeValidator.GetParentRecord<TParent>(
      db,
      ParentId,
      ParentService,
      _preloadedParent,
      _preloadedParentId,
      GetParent().BoundParentServiceType);

  public virtual bool CanDelete()
  {
    if (IsReadOnly)
    {
      return false;
    }

    var deleteMethod = GetType().GetMethod(nameof(Delete), BindingFlags.Public | BindingFlags.Instance)
      ?? GetType().GetMethod(nameof(DeleteAsync), BindingFlags.Public | BindingFlags.Instance);

    if (User is not { IsAuthenticated: true })
    {
      return false;
    }

    if (deleteMethod != null)
    {
      if (deleteMethod.GetCustomAttribute<RequireRightAttribute>(inherit: true) is { } reqRight)
      {
        var targetFeat = ModuleCatalog.Instance.FindFeature(reqRight.Service);
        return targetFeat != null &&
               (targetFeat.TryGetRight(reqRight.ActionName, out var right) && User.Can(right) ||
                User.Can(targetFeat.All));
      }

      if (deleteMethod.GetCustomAttribute<RequireActionAttribute>(inherit: true) is { } reqAction)
      {
        return Feature.TryGetRight(reqAction.ActionName, out var right) && User.Can(right) ||
               User.Can(Feature.All);
      }
    }

    return Feature.HasDelete && (User.Can(Feature.Delete) || User.Can(Feature.All));
  }

  public virtual Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public virtual IReadOnlyList<string>? GetReferenceLabelPaths() =>
    GetQueryDeclaration().ReferenceLabelPaths;

  public virtual IReadOnlyList<string>? GetLabelNavigationPaths() =>
    GetQueryDeclaration().LabelNavigationPaths;

  public virtual string? GetRecordLabel(object? entity)
  {
    if (entity is not TEntity typed)
    {
      return null;
    }

    var q = GetQueryDeclaration();
    if (q.CompiledLabelSelector != null)
    {
      try
      {
        if (typed.Id > 0 && q.LabelNavigationPaths is { Count: > 0 } navs && navs.Any(nav => IsNavigationPathUnloaded(typed, nav)))
        {
          var reloadQuery = RestrictedQuery().AsNoTracking();
          foreach (var nav in navs)
          {
            reloadQuery = reloadQuery.Include(nav);
          }

          typed = reloadQuery.FirstOrDefault(e => e.Id == typed.Id) ?? typed;
        }

        return q.CompiledLabelSelector(typed);
      }
      catch
      {
        return null;
      }
    }

    return null;
  }

  private bool IsNavigationPathUnloaded(object root, string navPath)
  {
    var current = root;
    foreach (var seg in navPath.Split('.'))
    {
      if (current == null)
      {
        return false;
      }

      var navValue = PropertyAccessorCache.GetValue(current, seg);
      if (navValue == null)
      {
        var fkName = seg + "Id";
        if (PropertyAccessorCache.GetProperty(current.GetType(), fkName) == null)
        {
          fkName = db.Model.FindEntityType(current.GetType())?.FindNavigation(seg)?.ForeignKey.Properties.FirstOrDefault()?.Name ?? fkName;
        }

        var fkValue = PropertyAccessorCache.GetValue(current, fkName);
        return fkValue switch
        {
          long l => l > 0,
          int i => i > 0,
          short s => s > 0,
          not null => true,
          _ => false
        };
      }

      current = navValue;
    }

    return false;
  }

  private static readonly ConcurrentDictionary<Type, bool> CustomShapeRowTypes = new();

  private bool HasCustomShapeRow =>
    CustomShapeRowTypes.GetOrAdd(GetType(), static t =>
    {
      var m1 = t.GetMethod("ShapeRow", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(TRow)]);
      var m3 = t.GetMethod(
        "ShapeRow",
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TRow), typeof(IReadOnlyList<GridColumnDto>), typeof(IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>)]);
      return m1 != null && m1.DeclaringType != typeof(GridService<TEntity, TRow>) ||
             m3 != null && m3.DeclaringType != typeof(GridService<TEntity, TRow>);
    });

  private (string? ParentService, long? ParentId, Query Value)? _cachedQuery;
  private (string? ParentService, long? ParentId, string ServicePath, Nav Value)? _cachedNav;
  private readonly Dictionary<string, OverlayDto> _cachedFormOverlays = new(StringComparer.OrdinalIgnoreCase);

  private Query GetQueryDeclaration()
  {
    if (_cachedQuery is { } cached && cached.ParentService == ParentService && cached.ParentId == ParentId)
    {
      return cached.Value;
    }

    var q = new Query();
    SetQuery(q);
    _cachedQuery = (ParentService, ParentId, q);
    return q;
  }

  private Nav GetNav(string? servicePath = null)
  {
    var effectivePath = servicePath ?? ServicePathResolver.Resolve(GetType());
    if (_cachedNav is { } cached &&
        cached.ParentService == ParentService &&
        cached.ParentId == ParentId &&
        string.Equals(cached.ServicePath, effectivePath, StringComparison.Ordinal))
    {
      return cached.Value;
    }

    var nav = new Nav(effectivePath, L);
    SetNav(nav);
    foreach (var (createFormType, _) in nav.Creates)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), createFormType, "Nav.AddCreate");
    }

    foreach (var (pickerSourceType, _, pickerModel) in nav.PickerEntries)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), pickerSourceType, $"Nav.AddPicker '{pickerModel.Key}'");
    }

    _cachedNav = (ParentService, ParentId, effectivePath, nav);
    return nav;
  }

  protected virtual void SetQuery(Query query) { }
  protected virtual void SetNav(Nav nav) { }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    SelectedIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    var nav = GetNav(ServicePathResolver.Resolve(GetType()));
    var entry = nav.Actions.FirstOrDefault(a =>
      string.Equals(a.Model.Key, actionKey, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(a.Model.Method, actionKey, StringComparison.OrdinalIgnoreCase));
    return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(entry?.DefaultProvider, entry?.Ask, db, PlatformApp, ct);
  }

  protected virtual void SetHooks(Hooks hooks) { }
  protected virtual void SetColumns(Columns columns) { }
  protected virtual void SetColumns(Columns<TRow> columns) { }

  public void RegisterMetadata() => CreateColumns();

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    _ = GetParent();
    var cols = CreateColumns();
    foreach (var col in cols.List)
    {
      if (col.PickerTargetMethod != null && !ServiceEndpointsExtensions.IsCallableServiceAction(col.PickerTargetMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{GetType().FullName}.{col.PickerTargetMethod.Name}' registered via PickedBy on '{col.Path}' must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }

    var nav = GetNav();
    foreach (var entry in nav.Actions)
    {
      if (!ServiceEndpointsExtensions.IsCallableServiceAction(entry.Method.Method))
      {
        throw new InvalidOperationException(
          $"Grid action method '{GetType().FullName}.{entry.Method.Method.Name}' registered via Nav.AddAction must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }

    foreach (var (_, pickerMethod, _) in nav.PickerEntries)
    {
      if (!ServiceEndpointsExtensions.IsCallableServiceAction(pickerMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{GetType().FullName}.{pickerMethod.Name}' registered via Nav.AddPicker must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }
  }

  private Columns CreateColumns()
  {
    var cols = new Columns();
    SetColumns(cols);
    SetColumns((Columns<TRow>)cols);
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.FormServiceType, "AsOpenForm / UseForm");
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.CoverFileGridType, "AsCover");
    if (cols.StatusFlow != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.StatusFlow.GetType(), "UseStatusOf");
    }

    foreach (var col in cols.List)
    {
      if (col.PickerSourceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), col.PickerSourceType, $"PickedBy on '{col.Path}'");
      }

      if (col.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), col.ReferenceServiceType, $"AsReference on '{col.Path}'");
      }
    }

    return cols;
  }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();

  private Type? ResolveFormServiceType(Columns columns)
  {
    if (columns.FormServiceType != null)
    {
      return columns.FormServiceType;
    }

    if (FormServicePath != null)
    {
      return ModuleCatalog.Instance.FindServiceType(FormServicePath);
    }

    var candidateFormName = GetType().Name.Replace("GridService", "FormService");
    return GetType().Assembly.GetType(GetType().Namespace + "." + candidateFormName)
      ?? GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateFormName);
  }

  private string? ResolveFormServicePath(Columns columns)
  {
    if (FormServicePath != null)
    {
      return FormServicePath;
    }

    var formType = ResolveFormServiceType(columns);
    return formType != null ? ServicePathResolver.Resolve(formType) : null;
  }

  public virtual IReadOnlyList<GridCreateDescriptor> GetCreateForms()
  {
    if (Feature.IsReadOnly || IsReadOnly && (_preloadedParentId.HasValue || GetParent().BoundProperty == null))
    {
      return [];
    }

    return ResolveDeclaredCreateForms();
  }

  public virtual IReadOnlyList<GridCreateDescriptor> GetAgentForms()
  {
    if (Feature.IsReadOnly)
    {
      return [];
    }

    var forms = GetCreateForms();
    return forms.Count > 0 ? forms : ResolveDeclaredCreateForms();
  }

  private IReadOnlyList<GridCreateDescriptor> ResolveDeclaredCreateForms()
  {
    var servicePath = ServicePathResolver.Resolve(GetType());
    var nav = GetNav(servicePath);
    if (nav.Creates.Count > 0)
    {
      return nav.Creates
        .Select(c => new GridCreateDescriptor(
          c.FormType,
          c.Model.Service ?? ServicePathResolver.Resolve(c.FormType),
          c.Model.Label ?? c.Model.Title ?? L["Add"],
          c.Model.Color))
        .ToList();
    }

    var columns = CreateColumns();
    var formType = ResolveFormServiceType(columns);
    var formPath = ResolveFormServicePath(columns);
    if (formType != null && !string.IsNullOrEmpty(formPath))
    {
      return [new GridCreateDescriptor(formType, formPath, L[Feature.Label], UiColor.Orange)];
    }

    return [];
  }
}
