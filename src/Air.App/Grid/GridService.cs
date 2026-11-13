using System.Runtime.CompilerServices;
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
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected internal Localizer L => Localizer.Instance.For(Feature.Module);
  public abstract Feature Feature { get; }

  protected virtual bool IsReadOnly =>
    Feature.IsReadOnly ||
    ResolveParentChildBuilder()?.IsReadOnly == true ||
    User is not { IsAuthenticated: true } ||
    !ServiceEndpointsExtensions.CanEdit(Feature, User);

  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  protected internal string? ParentService { get; set; }
  protected internal long? ParentId { get; set; }
  protected IReadOnlyList<long> SelectedIds { get; set; } = [];
  public bool AllowsMultiDelete => GetHooks().IsMultiDeleteAllowed;

  internal GridExecutionState<TEntity, TRow> State { get; } = new();
  internal bool IsGridReadOnly => IsReadOnly;
  internal string GridFiltersWidth => FiltersWidth;
  internal IReadOnlyList<long> CurrentSelectedIds { get => SelectedIds; set => SelectedIds = value; }
  internal TimeProvider TimeClock => Time;
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

  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  protected virtual void SetUserRestrictions(UserRestrictions restrictions) { }
  protected virtual void SetParent(Parent parent) { }
  protected virtual void SetQuery(Query query) { }
  protected virtual void SetNav(Nav nav) { }
  protected virtual void SetActions(Actions actions) { }

  protected virtual Task SetActionsAsync(Actions actions, CancellationToken ct = default)
  {
    SetActions(actions);
    return Task.CompletedTask;
  }

  protected virtual void SetHooks(Hooks hooks) { }
  protected virtual void SetColumns(Columns columns) { }
  protected virtual void SetColumns(Columns<TRow> columns) { }
  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();
  protected virtual OverlayDto? ResolveFormOverlay(Columns columns) => null;

  protected virtual async Task<OverlayDto?> ResolveFormOverlayAsync(Columns columns, CancellationToken ct = default)
  {
    if (ResolveFormOverlay(columns) is { } customOverlay)
    {
      return customOverlay;
    }

    var detectedFormService = ResolveFormServicePath(columns);
    if (detectedFormService is null)
    {
      return null;
    }

    var formType = ResolveFormServiceType(columns) ?? ModuleCatalog.Instance.FindServiceType(detectedFormService);
    return await GridScreenBuilder.ResolveFormOverlayForTypeAsync(this, formType, detectedFormService, ct);
  }

  protected virtual RowDto ShapeRow(
    TRow row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null) =>
    GridRowShaper.ShapeDefaultRow(this, row, columns, rowFlags);

  protected Parent GetParent()
  {
    if (State.CachedParent != null)
    {
      return State.CachedParent;
    }

    var parent = new Parent();
    SetParent(parent);
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), parent.BoundParentServiceType, "Parent.Bind/Read");
    return State.CachedParent = parent;
  }

  protected Hooks GetHooks()
  {
    if (State.CachedHooks != null)
    {
      return State.CachedHooks;
    }

    var hooks = new Hooks(Feature.Module);
    SetHooks(hooks);
    return State.CachedHooks = hooks;
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
    var query = GridQueryExecutor.BuildBaseEntityQuery(this, null, out _, out var queryDeclaration, preserveParent: true);
    if (queryDeclaration.LabelSelector != null)
    {
      foreach (var labelNav in Elements.ExtractNavigationPaths(queryDeclaration.LabelSelector))
      {
        query = query.Include(labelNav);
      }
    }

    return query;
  }

  /// <summary>Applies BaseQuery, SetUserRestrictions, and SetQuery filters without requiring a single ParentId scope.</summary>
  protected IQueryable<TEntity> UnscopedRestrictedQuery()
  {
    var query = BaseQuery(db);
    var restrictions = new UserRestrictions();
    SetUserRestrictions(restrictions);
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

  protected Task<IReadOnlyList<long>> InferBoundParentIdFromIdsAsync(
    IReadOnlyList<long> ids,
    CancellationToken ct,
    bool includeQueryFilters = false) =>
    GridMutationRunner.InferBoundParentIdFromIdsAsync(this, ids, ct, includeQueryFilters);

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
    State.CacheParentScope(
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
    if (Feature.IsReadOnly || IsReadOnly && (State.PreloadedParentId.HasValue || GetParent().BoundProperty == null))
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

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) =>
    GridScreenBuilder.BuildScreenAsync(this, servicePath, request, ct);

  public virtual Task<GridPageDto<RowDto>> GetDataAsync(ListRequest request, CancellationToken ct = default) =>
    GridQueryExecutor.GetDataAsync(this, request, ct);

  public virtual Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default) =>
    GridQueryExecutor.GetCountAsync(this, request, ct);

  public virtual Task<RowDto?> ReadRowAsync(long id, CancellationToken ct = default) =>
    GridMutationRunner.ReadRowAsync(this, id, ct);

  public virtual Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default) =>
    GridMutationRunner.SaveCellAsync(this, request, services, ct);

  public virtual Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public virtual Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default) =>
    GridMutationRunner.DeleteAsync(this, request, ct);

  public virtual Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default) =>
    GridMutationRunner.ExportAsync(this, request, ct);

  public virtual Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default) =>
    GridActionRunner.HasActionAsync(this, actionKey, ct);

  public virtual Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default) =>
    GridActionRunner.GetActionInputAsync(this, actionKey, request, ct);

  public virtual Task<GridActionDto?> ExecuteDeclaredActionAsync(string actionKey, GridRequest request, JsonElement? body, CancellationToken ct = default) =>
    GridActionRunner.ExecuteDeclaredActionAsync(this, actionKey, request, body, ct);

  public virtual Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default) =>
    GridActionRunner.MoveStatusAsync(this, request, transitionName, body, ct);

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    GridPickExecutor.ExecutePickedAsync(this, request, isAttach: true, handler, cancellationToken, actionName);

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    GridPickExecutor.ExecutePickedAsync<TEntity, TRow, TPicked>(
      this,
      request,
      isAttach: true,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken,
      actionName);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    GridPickExecutor.ExecutePickedAsync(this, request, isAttach: false, handler, cancellationToken, actionName);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    GridPickExecutor.ExecutePickedAsync<TEntity, TRow, TPicked>(
      this,
      request,
      isAttach: false,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken,
      actionName);

  protected Task<GridActionDto> AttachPickedAsync(
    GridPickRequest request,
    Action<TEntity, long> bindPicked,
    Func<TEntity> factory,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> afterAttach,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null) =>
    GridPickExecutor.ExecutePickedCoreAsync(
      this,
      request,
      actionName,
      (parent, _) =>
      {
        var target = GridPickExecutor.InitializeAttachedEntity(this, factory(), parent);
        bindPicked(target, request.PickedId);
        db.Set<TEntity>().Add(target);
        return Task.FromResult<(TEntity?, GridActionDto?, IReadOnlyList<long>)>((target, null, [request.PickedId]));
      },
      (ctx, _, ct) => afterAttach(ctx, ct),
      cancellationToken);

  protected Task<GridActionDto> ReplacePickedAsync(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null) =>
    GridPickExecutor.ExecutePickedCoreAsync(
      this,
      request,
      actionName,
      async (_, ct) =>
      {
        var (target, refusal) = await GridPickExecutor.LoadPickedReplaceTargetAsync(this, request, ct);
        return (target, refusal, [request.PickedId]);
      },
      (ctx, _, ct) => handler(ctx, ct),
      cancellationToken);

  public void RegisterMetadata() => GridMetadataValidator.RegisterMetadata(this);
  public Task RegisterMetadataAsync(CancellationToken ct = default) => GridMetadataValidator.RegisterMetadataAsync(this, ct);

  IEnumerable<string> IPlatformService.GetDeclaredActions() =>
    ProbeActions().Entries
      .Select(e => e.Permission.GetDeclaredActionName(e.Handle))
      .Where(n => !string.IsNullOrWhiteSpace(n))!;

  IReadOnlyList<string> IGridService.GetRoutableActionMethods(ModuleCatalog catalog) =>
    GridMetadataValidator.GetRoutableActionMethods(this, catalog);

  PropertyInfo? IGridService.GetBoundParentProperty() => GetParent().BoundProperty;
  bool IGridService.HasStatusFlow() => ProbeColumnsSafe().StatusFlow is IRecordStatuses<TEntity>;
  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog) => GridMetadataValidator.ValidateConfiguration(this, catalog);

  internal Parent GetParentDeclaration() => GetParent();
  internal Hooks GetHooksDeclaration() => GetHooks();
  internal Columns CreateColumnsDeclaration() => CreateColumns();
  internal Columns ProbeColumnsSafe()
  {
    try
    {
      return CreateColumns();
    }
    catch (ParentNotLoadedException)
    {
      return new Columns();
    }
  }

  internal Nav ProbeNavSafe()
  {
    try
    {
      return GetNav();
    }
    catch (ParentNotLoadedException)
    {
      return new Nav(L);
    }
  }
  internal Nav GetNavDeclaration(string? servicePath = null) => GetNav(servicePath);
  internal IQueryable<TEntity> InvokeBaseQuery(PlatformDbContext context) => BaseQuery(context);
  internal void InvokeSetUserRestrictions(UserRestrictions restrictions) => SetUserRestrictions(restrictions);
  internal void InvokeSetActions(Actions actions) => SetActions(actions);
  internal Task InvokeSetActionsAsync(Actions actions, CancellationToken ct) => SetActionsAsync(actions, ct);
  internal Task<bool> EnsureParentScopeInternalAsync(bool lockParent, CancellationToken ct) => EnsureParentScopeAsync(lockParent, ct);
  internal IQueryable<TEntity> RestrictedQueryInternal() => RestrictedQuery();
  internal Task<IReadOnlyList<long>> InferBoundParentIdInternalAsync(IReadOnlyList<long> ids, CancellationToken ct, bool includeQueryFilters = false) =>
    InferBoundParentIdFromIdsAsync(ids, ct, includeQueryFilters);
  internal Task<OverlayDto?> InvokeResolveFormOverlayAsync(Columns columns, CancellationToken ct) => ResolveFormOverlayAsync(columns, ct);
  internal RowDto InvokeShapeRow(TRow row, IReadOnlyList<GridColumnDto> columns, IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null) =>
    ShapeRow(row, columns, rowFlags);

  internal static QueryMap<TEntity> BuildQueryMap(
    Type serviceType,
    Query queryDeclaration,
    IReadOnlyList<GridColumnDto> columns,
    bool useCollationComparison = false) =>
    GridQueryMapBuilder<TEntity, TRow>.Build(serviceType, queryDeclaration, columns, useCollationComparison);

  internal Query GetQueryDeclaration()
  {
    if (State.CachedQuery is { } cached && cached.ParentService == ParentService && cached.ParentId == ParentId)
    {
      return cached.Value;
    }

    var q = new Query();
    SetQuery(q);
    State.CachedQuery = (ParentService, ParentId, q);
    return q;
  }

  internal Actions ProbeActions()
  {
    var actions = new Actions();
    try
    {
      SetActionsAsync(actions).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0 && actions.InheritedFormServiceTypes.Count == 0)
      {
        try
        {
          SetActions(actions);
        }
        catch (ParentNotLoadedException)
        {
        }
      }

      var overridesAsync = GetType()
        .GetMethod(nameof(SetActionsAsync), BindingFlags.Instance | BindingFlags.NonPublic)?
        .DeclaringType != typeof(GridService<TEntity, TRow>);
      if (db == null && overridesAsync && actions.Entries.Count == 0 && actions.InheritedFormServiceTypes.Count == 0)
      {
        throw new InvalidOperationException(
          $"{GetType().Name}.{nameof(SetActionsAsync)} accessed null runtime dependencies during startup action discovery. Guard runtime-only queries with 'if (db != null)' before registering actions.",
          ex);
      }
    }

    return actions;
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

  internal Type? ResolveFormServiceType(Columns columns)
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

  internal string? ResolveFormServicePath(Columns columns)
  {
    if (FormServicePath != null)
    {
      return FormServicePath;
    }

    var formType = ResolveFormServiceType(columns);
    return formType != null ? ServicePathResolver.Resolve(formType) : null;
  }

  private Nav GetNav(string? servicePath = null)
  {
    var effectivePath = servicePath ?? ServicePathResolver.Resolve(GetType());
    if (State.CachedNav is { } cached &&
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

    State.CachedNav = (ParentService, ParentId, effectivePath, nav);
    return nav;
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
