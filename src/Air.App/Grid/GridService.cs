using Platform.App.Host.Internals;
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
  protected ModuleCatalog Catalog => PlatformApp?.Catalog ?? ModuleCatalog.Instance;
  protected ICultureCatalog Cultures => PlatformApp?.Cultures ?? CultureCatalog.DefaultInstance;
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

    var hooks = new Hooks(Feature.Module);
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

    var parent = GetParent();
    if (parent.BoundProperty != null && !parent.IsOptional && request.ParentId is null or <= 0)
    {
      return false;
    }

    if (typeof(BaseAppFile).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthRole).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthGrant).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthAssignment).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthSession).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppAgentMemory).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppAgentModel).IsAssignableFrom(typeof(TEntity)) ||
        typeof(AppPlatformChange).IsAssignableFrom(typeof(TEntity)))
    {
      return false;
    }

    return ReferenceLookupAllowlist.CanUserLookup(GetType(), Catalog, user);
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
    var parent = GetParent();
    if (!string.IsNullOrEmpty(parentService) || parentId.HasValue || parent.BoundProperty != null && !parent.IsOptional)
    {
      return await EnsureParentScopeAsync(lockParent: false, ct);
    }

    return true;
  }

  protected virtual async Task<bool> EnsureParentScopeAsync(bool lockParent, CancellationToken ct)
  {
    var parent = GetParent();
    var hasRequiredBoundParent = parent.BoundProperty != null && !parent.IsOptional;
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
        return q.CompiledLabelSelector(typed);
      }
      catch
      {
        return null;
      }
    }

    return null;
  }

  public virtual async Task<string?> GetRecordLabelAsync(object? entity, CancellationToken ct = default)
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

          typed = await reloadQuery.FirstOrDefaultAsync(e => e.Id == typed.Id, ct) ?? typed;
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
      var m = t.GetMethod(
        "ShapeRow",
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TRow), typeof(IReadOnlyList<GridColumnDto>), typeof(IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>)]);
      return m != null && m.DeclaringType != typeof(GridService<TEntity, TRow>);
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

    var nav = new Nav(L);
    SetNav(nav);
    foreach (var (createFormType, _) in nav.Creates)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), createFormType, "Nav.AddCreate");
    }

    foreach (var (pickerSourceType, _, pickerModel) in nav.PickerEntries)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), pickerSourceType, $"Nav.AddPicker '{pickerModel.Key}'");
      ReferenceLookupAllowlist.RegisterReferrer(pickerSourceType, GetType());
    }

    _cachedNav = (ParentService, ParentId, effectivePath, nav);
    return nav;
  }

  protected virtual void SetQuery(Query query) { }
  protected virtual void SetNav(Nav nav) { }
  protected virtual void SetActions(Actions actions) { }

  protected virtual Task SetActionsAsync(Actions actions, CancellationToken ct = default)
  {
    SetActions(actions);
    return Task.CompletedTask;
  }

  private Actions ProbeActions()
  {
    var actions = new Actions();
    try
    {
      SetActionsAsync(actions).GetAwaiter().GetResult();
    }
    catch (NullReferenceException ex) when (db == null)
    {
      actions = new Actions();
      SetActions(actions);
      var overridesAsync = GetType()
        .GetMethod(nameof(SetActionsAsync), BindingFlags.Instance | BindingFlags.NonPublic)?
        .DeclaringType != typeof(GridService<TEntity, TRow>);
      if (overridesAsync && actions.Entries.Count == 0 && actions.InheritedFormServiceTypes.Count == 0)
      {
        throw new InvalidOperationException(
          $"{GetType().Name}.{nameof(SetActionsAsync)} accessed null runtime dependencies during startup action discovery. Guard runtime-only queries with 'if (db != null)' before registering actions.",
          ex);
      }
    }

    return actions;
  }

  IEnumerable<string> IPlatformService.GetDeclaredActions() =>
    ProbeActions().Entries
      .Select(e => e.Permission.GetDeclaredActionName(e.Handle))
      .Where(n => !string.IsNullOrWhiteSpace(n))!;

  IReadOnlyList<string> IGridService.GetRoutableActionMethods(ModuleCatalog catalog)
  {
    var actions = ProbeActions();
    var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      routes.Add(entry.Method);
      routes.Add(entry.Key);
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (catalog.CreateServiceInstance(formType) is IFormService formProbe)
      {
        foreach (var inherited in formProbe.GetRoutableActionMethods(catalog))
        {
          routes.Add(inherited);
        }
      }
    }

    AppServiceRouteMapper.AddFeatureCustomRightRoutes(routes, Feature);
    return [.. routes];
  }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default)
  {
    if (!await BindParentAsync(request.ParentService, request.ParentId, ct))
    {
      return null;
    }

    var normalizedIds = NormalizeSelection(request).Ids;
    if (normalizedIds.Count > 0)
    {
      var reachableSet = (await RestrictedQuery()
        .Where(e => normalizedIds.Contains(e.Id))
        .Select(e => e.Id)
        .ToListAsync(ct)).ToHashSet();
      SelectedIds = normalizedIds.Where(reachableSet.Contains).ToList();
      if (SelectedIds.Count != normalizedIds.Count)
      {
        return null;
      }
    }
    else
    {
      SelectedIds = [];
    }

    var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
    var actions = new Actions();
    await SetActionsAsync(actions, ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    if (entry != null)
    {
      if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly) ||
          entry.NeedsSelection && SelectedIds.Count == 0 ||
          entry.IsSingle && SelectedIds.Count != 1)
      {
        return null;
      }

      var defaultProvider = entry.DefaultProvider != null
        ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(SelectedIds, token))
        : null;
      return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, entry.Ask, db, PlatformApp, ct);
    }

    if (isParentReadOnly)
    {
      return null;
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        if (PlatformApp != null)
        {
          form.AttachPlatformApp(PlatformApp);
        }

        if (await form.HasActionAsync(actionKey, ct))
        {
          var formReq = new FormRequest
          {
            Id = SelectedIds.Count == 1 ? SelectedIds[0] : request.Id,
            ParentId = ParentId,
            ParentService = ParentService
          };
          return await form.GetActionInputAsync(actionKey, formReq, ct);
        }
      }
    }

    return null;
  }

  protected virtual void SetHooks(Hooks hooks) { }
  protected virtual void SetColumns(Columns columns) { }
  protected virtual void SetColumns(Columns<TRow> columns) { }

  PropertyInfo? IGridService.GetBoundParentProperty() => GetParent().BoundProperty;

  public void RegisterMetadata()
  {
    var cols = CreateColumns();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    var actions = new Actions();
    try
    {
      SetActions(actions);
    }
    catch
    {
    }

    foreach (var entry in actions.Entries)
    {
      entry.Ask?.BuildElements();
    }
  }

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    var cols = CreateColumns();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    var actions = new Actions();
    try
    {
      await SetActionsAsync(actions, ct);
    }
    catch
    {
      actions = new Actions();
      try
      {
        SetActions(actions);
      }
      catch
      {
      }
    }

    foreach (var entry in actions.Entries)
    {
      entry.Ask?.BuildElements();
    }
  }

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
    foreach (var (pickerSourceType, pickerMethod, _) in nav.PickerEntries)
    {
      ReferenceLookupAllowlist.RegisterReferrer(pickerSourceType, GetType());
      if (!ServiceEndpointsExtensions.IsCallableServiceAction(pickerMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{GetType().FullName}.{pickerMethod.Name}' registered via Nav.AddPicker must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }

    var actions = new Actions();
    try
    {
      SetActionsAsync(actions).GetAwaiter().GetResult();
    }
    catch when (db == null)
    {
      actions = new Actions();
      SetActions(actions);
    }

    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      if (!seenHandles.Add(entry.Handle))
      {
        throw new InvalidOperationException(
          $"Grid '{GetType().FullName}' declares duplicate action handle '{entry.Handle}'.");
      }

      entry.ValidateConfiguration(GetType(), Feature);
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), formType, "Actions.UseActionsOf");
      var formEntityType = ParentScopeValidator.GetFormEntityType(formType);
      if (formEntityType != typeof(TEntity))
      {
        throw new InvalidOperationException(
          $"Grid '{GetType().FullName}' cannot call UseActionsOf<{formType.Name}>() because entity type '{formEntityType?.Name}' does not match '{typeof(TEntity).Name}'.");
      }
    }

    var hooks = GetHooks();
    if (hooks.DependentEntityTypes.Count > 0)
    {
      var dbType = ModuleCatalog.GetServiceDbContextType(GetType());
      if (dbType != null && dbType != typeof(PlatformDbContext))
      {
        var dbProps = dbType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var depType in hooks.DependentEntityTypes)
        {
          var expectedSetType = typeof(DbSet<>).MakeGenericType(depType);
          if (!dbProps.Any(p => p.PropertyType == expectedSetType))
          {
            throw new InvalidOperationException(
              $"Grid '{GetType().FullName}' registers PreventDeleteWhenUsedBy<{depType.Name}>(), but DbContext '{dbType.Name}' does not expose 'DbSet<{depType.Name}>'. Add 'public DbSet<{depType.Name}> ... => Set<{depType.Name}>();' to '{dbType.Name}'.");
          }
        }
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

    string? resolvedParentFormPath = null;
    foreach (var col in cols.List)
    {
      if (col.PickerSourceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), col.PickerSourceType, $"PickedBy on '{col.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(col.PickerSourceType, GetType());
      }

      if (col.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), col.ReferenceServiceType, $"AsReference on '{col.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(col.ReferenceServiceType, GetType());
      }

      if (col.Cell is GridCellCoverModel { Service.Length: > 0, ParentService.Length: 0 } cover)
      {
        resolvedParentFormPath ??= ResolveFormServicePath(cols) ?? string.Empty;
        cover.ParentService = resolvedParentFormPath;
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

    return ParentScopeValidator.ResolveConventionFormType(GetType());
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
