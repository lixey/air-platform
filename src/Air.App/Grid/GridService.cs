using Platform.App.Grid.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>(PlatformDbContext db, PlatformAppService? app = null) : IGridService
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private static readonly ConcurrentDictionary<Type, bool> CustomShapeRowTypes = new();

  public PlatformDbContext DbContext => db;
  public PlatformAppService? PlatformApp { get; private set; } = app;
  protected internal IUserContext User => PlatformApp?.User ?? new UserContext(new HttpContextAccessor());
  protected internal ModuleCatalog Catalog => PlatformApp?.Catalog ?? ModuleCatalog.Instance;
  protected ICultureCatalog Cultures => PlatformApp?.Cultures ?? CultureCatalog.DefaultInstance;
  protected TimeProvider Time => PlatformApp?.Time ?? db.Time;
  protected internal Localizer L => (PlatformApp?.L as Localizer ?? Localizer.Instance).For(Feature.Module);
  public abstract Feature Feature { get; }

  protected internal Columns<TEntity, TRow> Columns { get; private set; } = new();
  protected internal GridQuery<TEntity, TRow> Query { get; private set; } = new();
  protected internal GridNav Nav { get; private set; } = new();
  protected internal GridActions<TEntity> Actions { get; private set; } = new();
  protected internal GridHooks<TEntity> Hooks { get; private set; } = new();
  protected internal GridParent<TEntity> Parent { get; private set; } = new();
  protected internal GridRestrictions<TEntity> Restrictions { get; private set; } = new();

  private bool _probingServiceLock;

  internal bool IsServiceLockedReadOnly
  {
    get
    {
      _probingServiceLock = true;
      try
      {
        return IsReadOnly;
      }
      finally
      {
        _probingServiceLock = false;
      }
    }
  }

  protected virtual bool IsReadOnly =>
    typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) ||
    User is not { IsAuthenticated: true } ||
    !_probingServiceLock && (
      Feature.IsReadOnly ||
      ResolveParentChildBuilder()?.IsReadOnly == true ||
      !ServiceEndpointsExtensions.CanEdit(Feature, User));

  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  protected internal string? ParentService { get; set; }
  protected internal long? ParentId { get; set; }
  protected IReadOnlyList<long> SelectedIds { get; set; } = [];
  public bool AllowsMultiDelete => GetHooks().IsMultiDeleteAllowed;

  internal GridExecutionState<TEntity, TRow> State { get; } = new();
  protected IDictionary<string, object?> ExecutionItems => State.Items;

  protected T? GetExecutionItem<T>(string key) => State.GetItem<T>(key);

  protected void SetExecutionItem<T>(string key, T value) => State.SetItem(key, value);

  protected T? GetExecutionItem<T>() =>
    GetExecutionItem<T>(typeof(T).FullName ?? typeof(T).Name);

  protected void SetExecutionItem<T>(T value) =>
    SetExecutionItem(typeof(T).FullName ?? typeof(T).Name, value);
  internal bool IsSqlServerProvider => db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

  internal bool HasCustomShapeRow =>
    CustomShapeRowTypes.GetOrAdd(GetType(), static t =>
    {
      var m = t.GetMethod(
        nameof(ShapeRow),
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(TRow), typeof(IReadOnlyList<GridColumnDto>), typeof(IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>)]);
      return m != null && m.DeclaringType != typeof(GridService<TEntity, TRow>);
    });

  protected virtual void SetRestrictions() { }
  protected virtual void SetParent() { }
  protected virtual void SetQuery() { }
  protected virtual void SetHooks() { }
  protected virtual void SetColumns() { }

  protected virtual Task SetColumnsAsync(CancellationToken cancellationToken = default)
  {
    SetColumns();
    return Task.CompletedTask;
  }

  protected virtual void SetNav() { }

  protected virtual Task SetNavAsync(CancellationToken cancellationToken = default)
  {
    SetNav();
    return Task.CompletedTask;
  }

  protected virtual void SetActions() { }

  protected virtual Task SetActionsAsync(CancellationToken cancellationToken = default)
  {
    SetActions();
    return Task.CompletedTask;
  }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();
  protected virtual OverlayDto? ResolveFormOverlay() => null;

  protected virtual async Task<OverlayDto?> ResolveFormOverlayAsync(CancellationToken ct = default)
  {
    var cols = await CreateColumnsAsync(ct);

    if (ResolveFormOverlay() is { } customOverlay)
    {
      return customOverlay;
    }

    var detectedFormService = ResolveFormServicePath(cols);
    if (detectedFormService is null)
    {
      return null;
    }

    var formType = ResolveFormServiceType(cols) ?? ModuleCatalog.Instance.FindServiceType(detectedFormService);
    return await ResolveFormOverlayForTypeAsync(formType, detectedFormService, ct);
  }

  protected virtual RowDto ShapeRow(
    TRow row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null) =>
    GridRowShaper.ShapeDefaultRow(this, row, columns, rowFlags);

  internal IQueryable<TEntity> ReachableEntities() =>
    BuildBaseEntityQuery(null, out _, out _, preserveParent: true).AsNoTracking();

  internal RowDto ShapeRowInternal(
    TRow row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null) =>
    ShapeRow(row, columns, rowFlags);

  protected GridParent<TEntity> GetParent()
  {
    if (State.CachedParent != null)
    {
      return Parent = State.CachedParent;
    }

    Parent = new GridParent<TEntity>();
    SetParent();
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), Parent.BoundParentServiceType, "Parent.Bind/Read");
    return State.CachedParent = Parent;
  }

  protected GridHooks<TEntity> GetHooks()
  {
    if (State.CachedHooks != null)
    {
      return Hooks = State.CachedHooks;
    }

    Hooks = new GridHooks<TEntity>(Feature.Module);
    SetHooks();
    return State.CachedHooks = Hooks;
  }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity =>
    ParentScopeValidator.GetParentRecord<TParent>(
      db,
      ParentId,
      ParentService,
      State.PreloadedParent,
      State.PreloadedParentId,
      GetParent().BoundParentServiceType);

  protected TParent RequiredParent<TParent>() where TParent : class, IEntity =>
    ParentRecord<TParent>() ?? throw new ParentNotLoadedException(typeof(TParent), GetType());

  protected IQueryable<TEntity> Reachable(bool asTracking = false)
  {
    var q = RestrictedQuery();
    return asTracking ? q.AsTracking() : q.AsNoTracking();
  }

  protected IQueryable<TEntity> Selected(bool asTracking = false) =>
    SelectedIds.Count > 0
      ? Reachable(asTracking).Where(e => SelectedIds.Contains(e.Id))
      : Reachable(asTracking).Where(_ => false);

  protected IQueryable<TEntity> RestrictedQuery()
  {
    var query = BuildBaseEntityQuery(null, out _, out var queryDeclaration, preserveParent: true);
    if (queryDeclaration.LabelSelector != null)
    {
      foreach (var labelNav in Form.Elements.ExtractNavigationPaths(queryDeclaration.LabelSelector))
      {
        query = query.Include(labelNav);
      }
    }

    return query;
  }

  /// <summary>Applies BaseQuery, SetRestrictions, and SetQuery filters without requiring a single ParentId scope.</summary>
  protected IQueryable<TEntity> UnscopedRestrictedQuery()
  {
    var query = BaseQuery(db);
    var restrictions = BuildRestrictions();
    foreach (var restriction in restrictions.Filters)
    {
      query = query.Where(restriction);
    }

    foreach (var filter in GetQueryDeclaration().Filters)
    {
      query = query.Where(filter);
    }

    return query;
  }

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
    if (!lockParent &&
        State.CachedParentValidation is { } cachedParent &&
        string.Equals(cachedParent.ParentService, ParentService, StringComparison.OrdinalIgnoreCase) &&
        cachedParent.ParentId == ParentId)
    {
      State.PreloadedParent = cachedParent.Preloaded;
      State.PreloadedParentId = cachedParent.ParentId;
      return cachedParent.Allowed;
    }

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
    State.CacheParentScope(
      allowed,
      preloaded,
      ParentId,
      ParentService,
      parent.BoundParentService,
      parentChildren);

    return allowed;
  }

  public virtual bool CanView(GridRequest request, IUserContext? user) =>
    ServiceEndpointsExtensions.CanView(Feature, user);

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
        typeof(AppPlatformChange).IsAssignableFrom(typeof(TEntity)))
    {
      return false;
    }

    return ReferenceLookupAllowlist.CanUserLookup(GetType(), Catalog, user);
  }

  public virtual bool CanDelete()
  {
    if (IsReadOnly || User is not { IsAuthenticated: true })
    {
      return false;
    }

    var deleteMethod = GetType().GetMethod(nameof(Delete), BindingFlags.Public | BindingFlags.Instance)
      ?? GetType().GetMethod(nameof(DeleteAsync), BindingFlags.Public | BindingFlags.Instance);
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

  public virtual IReadOnlyList<GridCreateDescriptor> GetCreateForms()
  {
    if (Feature.IsReadOnly ||
        typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) ||
        IsReadOnly && (State.PreloadedParentId.HasValue || GetParent().BoundProperty == null))
    {
      return [];
    }

    var nav = GetNavSync(ServicePathResolver.Resolve(GetType()));
    return nav.ShowCreate ? ResolveDeclaredCreateForms(nav) : [];
  }

  public virtual async Task<IReadOnlyList<GridCreateDescriptor>> GetCreateFormsAsync(CancellationToken ct = default)
  {
    if (Feature.IsReadOnly ||
        typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) ||
        IsReadOnly && (State.PreloadedParentId.HasValue || GetParent().BoundProperty == null))
    {
      return [];
    }

    var nav = await GetNavAsync(ServicePathResolver.Resolve(GetType()), ct);
    return nav.ShowCreate ? await ResolveDeclaredCreateFormsAsync(nav, ct) : [];
  }

  public virtual IReadOnlyList<GridCreateDescriptor> GetAgentForms()
  {
    if (Feature.IsReadOnly || typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)))
    {
      return [];
    }

    var forms = GetCreateForms();
    return forms.Count > 0 ? forms : ResolveDeclaredCreateForms();
  }

  public virtual async Task<IReadOnlyList<GridCreateDescriptor>> GetDeclaredFormsAsync(CancellationToken ct = default)
  {
    if (Feature.IsReadOnly || typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)))
    {
      return [];
    }

    var forms = await GetCreateFormsAsync(ct);
    return forms.Count > 0 ? forms : await ResolveDeclaredCreateFormsAsync(existingNav: null, ct);
  }

  internal Form.ChildGridBuilder? ResolveParentChildBuilder()
  {
    var myService = ServicePathResolver.Resolve(GetType());
    return ParentScopeValidator.ResolveChildBuilder(
      ParentService,
      GetParent().BoundParentService,
      ParentId,
      State.CachedParentChildren,
      c => c.ServiceType == GetType() || string.Equals(c.ServicePath, myService, StringComparison.OrdinalIgnoreCase));
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
}
