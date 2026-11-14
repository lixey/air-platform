using Platform.App.Grid.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  private GridRestrictions<TEntity> BuildRestrictions()
  {
    if (State.CachedRestrictions is { } cached &&
        string.Equals(cached.ParentService, ParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == ParentId)
    {
      return Restrictions = cached.Value;
    }

    Restrictions = new GridRestrictions<TEntity>();
    var restrictions = Restrictions;
    SetRestrictions();
    State.CachedRestrictions = (ParentService, ParentId, restrictions);
    return restrictions;
  }

  internal GridQuery<TEntity, TRow> GetQueryDeclaration()
  {
    if (State.CachedQuery is { } cached &&
        string.Equals(cached.ParentService, ParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == ParentId)
    {
      return Query = cached.Value;
    }

    Query = new GridQuery<TEntity, TRow>();
    var query = Query;
    SetQuery();
    State.CachedQuery = (ParentService, ParentId, query);
    return query;
  }

  internal Task<GridActions<TEntity>> BuildActionsInternalAsync(CancellationToken ct = default) =>
    BuildActionsAsync(ct);

  private async Task<GridActions<TEntity>> BuildActionsAsync(CancellationToken ct = default, bool bypassCache = false)
  {
    var selectedKey = SelectedIds.Count == 0 ? string.Empty : string.Join(',', SelectedIds);
    if (!bypassCache &&
        State.CachedActions is { } cached &&
        string.Equals(cached.ParentService, ParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == ParentId &&
        string.Equals(cached.SelectedKey, selectedKey, StringComparison.Ordinal))
    {
      return Actions = cached.Value;
    }

    Actions = new GridActions<TEntity>();
    var actions = Actions;
    await SetActionsAsync(ct);
    State.CachedActions = (ParentService, ParentId, selectedKey, actions);
    return actions;
  }

  private async Task<Columns<TEntity, TRow>> CreateColumnsAsync(CancellationToken ct = default)
  {
    if (State.CachedColumns is { } cached &&
        string.Equals(cached.ParentService, ParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == ParentId)
    {
      return Columns = cached.Value;
    }

    Columns = new Columns<TEntity, TRow>();
    var cols = Columns;
    await SetColumnsAsync(ct);
    FinalizeColumns(cols);
    State.CachedColumns = (ParentService, ParentId, cols);
    return cols;
  }

  private Columns<TEntity, TRow> CreateColumnsSync()
  {
    Columns = new Columns<TEntity, TRow>();
    var cols = Columns;
    if (!ServiceOverrideFlags.For(GetType()).OverridesSetColumnsAsync)
    {
      try
      {
        SetColumns();
      }
      catch (Exception ex) when (db == null && ex is not ParentNotLoadedException)
      {
      }
    }

    FinalizeColumns(cols);
    return cols;
  }

  private void FinalizeColumns(Columns<TEntity, TRow> cols)
  {
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.FormServiceType, "AsOpenForm / UseForm");
    ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.CoverFileGridType, "AsCover");
    if (cols.StatusFlow != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), cols.StatusFlow.GetType(), "UseStatusOf");
      (cols.StatusFlow as IPlatformService)?.BindContext(PlatformApp);
    }

    string? resolvedParentFormPath = null;
    foreach (var col in cols.List)
    {
      var runtime = GridColumnRuntime.TryGet(col);
      if (runtime?.PickerSourceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), runtime.PickerSourceType, $"PickedBy on '{col.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(runtime.PickerSourceType, GetType());
      }

      if (runtime?.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), runtime.ReferenceServiceType, $"AsReference on '{col.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(runtime.ReferenceServiceType, GetType());
      }

      if (col.Cell is GridCellCoverModel { Service.Length: > 0, ParentService.Length: 0 } cover)
      {
        resolvedParentFormPath ??= ResolveFormServicePath(cols) ?? string.Empty;
        cover.ParentService = resolvedParentFormPath;
      }
    }
  }

  private Columns<TEntity, TRow> ProbeColumnsSafe()
  {
    try
    {
      return CreateColumnsSync();
    }
    catch (ParentNotLoadedException)
    {
      return new Columns<TEntity, TRow>();
    }
  }

  private async Task<GridNav> GetNavAsync(string? servicePath = null, CancellationToken ct = default)
  {
    var effectivePath = servicePath ?? ServicePathResolver.Resolve(GetType());
    if (State.CachedNav is { } cached &&
        cached.ParentService == ParentService &&
        cached.ParentId == ParentId &&
        string.Equals(cached.ServicePath, effectivePath, StringComparison.Ordinal))
    {
      return Nav = cached.Value;
    }

    Nav = new GridNav(L);
    var nav = Nav;
    await SetNavAsync(ct);
    FinalizeNav(nav, effectivePath);
    return nav;
  }

  private GridNav GetNavSync(string? servicePath = null)
  {
    var effectivePath = servicePath ?? ServicePathResolver.Resolve(GetType());
    if (State.CachedNav is { } cached &&
        cached.ParentService == ParentService &&
        cached.ParentId == ParentId &&
        string.Equals(cached.ServicePath, effectivePath, StringComparison.Ordinal))
    {
      return Nav = cached.Value;
    }

    Nav = new GridNav(L);
    var nav = Nav;
    if (!ServiceOverrideFlags.For(GetType()).OverridesSetGridNavAsync)
    {
      try
      {
        SetNav();
      }
      catch (Exception ex) when (db == null && ex is not ParentNotLoadedException)
      {
      }
    }

    FinalizeNav(nav, effectivePath);
    return nav;
  }

  private void FinalizeNav(GridNav nav, string effectivePath)
  {
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
  }

  private GridNav ProbeNavSafe()
  {
    try
    {
      return GetNavSync();
    }
    catch (ParentNotLoadedException)
    {
      return new GridNav(L);
    }
  }

  private GridActions<TEntity> ProbeActionsSync()
  {
    Actions = new GridActions<TEntity>();
    var actions = Actions;
    if (ServiceOverrideFlags.For(GetType()).OverridesSetGridActionsAsync)
    {
      return actions;
    }

    try
    {
      SetActions();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
    }

    return actions;
  }

  private bool HasInheritedAsyncFormActions() =>
    ProbeActionsSync().InheritedFormServiceTypes.Any(static t => ServiceOverrideFlags.For(t).OverridesSetFormActionsAsync);

  private async Task<GridActions<TEntity>> ProbeActionsAsync(CancellationToken ct)
  {
    Actions = new GridActions<TEntity>();
    var actions = Actions;
    try
    {
      await SetActionsAsync(ct);
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0 &&
          actions.InheritedFormServiceTypes.Count == 0 &&
          !ServiceOverrideFlags.For(GetType()).OverridesSetGridActionsAsync)
      {
        try
        {
          SetActions();
        }
        catch (ParentNotLoadedException)
        {
        }
      }
    }

    return actions;
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

  private IReadOnlyList<GridCreateDescriptor> ResolveDeclaredCreateForms(GridNav? existingNav = null)
  {
    var servicePath = ServicePathResolver.Resolve(GetType());
    var nav = existingNav ?? GetNavSync(servicePath);
    return MapCreateForms(nav, ProbeColumnsSafe());
  }

  private async Task<IReadOnlyList<GridCreateDescriptor>> ResolveDeclaredCreateFormsAsync(
    GridNav? existingNav = null,
    CancellationToken ct = default)
  {
    var servicePath = ServicePathResolver.Resolve(GetType());
    var nav = existingNav ?? await GetNavAsync(servicePath, ct);
    if (nav.Creates.Count > 0)
    {
      return MapCreateForms(nav, Columns);
    }

    var columns = await CreateColumnsAsync(ct);
    return MapCreateForms(nav, columns);
  }

  private IReadOnlyList<GridCreateDescriptor> MapCreateForms(GridNav nav, Columns<TEntity, TRow> columns)
  {
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

    var formType = ResolveFormServiceType(columns);
    var formPath = ResolveFormServicePath(columns);
    if (formType != null && !string.IsNullOrEmpty(formPath))
    {
      return [new GridCreateDescriptor(formType, formPath, L[Feature.Label], UiColor.Orange)];
    }

    return [];
  }

  public void RegisterMetadata()
  {
    var cols = ProbeColumnsSafe();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    foreach (var entry in ProbeActionsSync().Entries)
    {
      entry.Ask?.ValidateConfiguration(GetType());
    }
  }

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    Columns<TEntity, TRow> cols;
    try
    {
      cols = db != null ? await CreateColumnsAsync(ct) : ProbeColumnsSafe();
    }
    catch (ParentNotLoadedException)
    {
      cols = new Columns<TEntity, TRow>();
    }

    if (db != null)
    {
      try
      {
        _ = await GetNavAsync(ct: ct);
      }
      catch (ParentNotLoadedException)
      {
      }
    }
    else
    {
      _ = ProbeNavSafe();
    }

    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    foreach (var entry in (await ProbeActionsAsync(ct)).Entries)
    {
      entry.Ask?.ValidateConfiguration(GetType());
    }
  }

  IEnumerable<string> IPlatformService.GetDeclaredActions() =>
    ProbeActionsSync().Entries
      .Select(e => e.Permission.GetDeclaredActionName(e.Handle))
      .Where(n => !string.IsNullOrWhiteSpace(n))!;

  IReadOnlyList<string> IGridService.GetRoutableActionMethods(ModuleCatalog catalog)
  {
    var actions = ProbeActionsSync();
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

    StatusMoveNavBuilder.AddStatusAndFeatureRoutes(
      routes,
      ProbeColumnsSafe().StatusFlow as IRecordStatuses,
      Feature,
      typeof(TEntity),
      catalog);
    return [.. routes];
  }

  PropertyInfo? IGridService.GetBoundParentProperty() => GetParent().BoundProperty;
  bool IGridService.HasStatusFlow() => ProbeColumnsSafe().StatusFlow is IRecordStatuses<TEntity>;

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    var gridType = GetType();
    if (typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) && !Feature.IsReadOnly)
    {
      throw new InvalidOperationException(
        $"Grid '{gridType.FullName}' over IReadOnlyEntity '{typeof(TEntity).Name}' requires feature '{Feature.Key}' to be declared .ReadOnly().");
    }

    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      gridType,
      typeof(GridService<,>),
      (nameof(SetColumns), nameof(SetColumnsAsync)),
      (nameof(SetNav), nameof(SetNavAsync)),
      (nameof(SetActions), nameof(SetActionsAsync)));

    _ = GetParent();
    try
    {
      _ = GetQueryDeclaration();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
    }

    var cols = ProbeColumnsSafe();
    foreach (var col in cols.List)
    {
      var pickerTargetMethod = GridColumnRuntime.TryGet(col)?.PickerTargetMethod;
      if (pickerTargetMethod != null && !ServiceEndpointsExtensions.IsEndpointMethod(pickerTargetMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{gridType.FullName}.{pickerTargetMethod.Name}' registered via PickedBy on '{col.Path}' must be public and decorated with [Endpoint], [RequireAction], or [RequireRight].");
      }
    }

    var nav = ProbeNavSafe();
    foreach (var (pickerSourceType, pickerMethod, _) in nav.PickerEntries)
    {
      ReferenceLookupAllowlist.RegisterReferrer(pickerSourceType, gridType);
      if (!ServiceEndpointsExtensions.IsEndpointMethod(pickerMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{gridType.FullName}.{pickerMethod.Name}' registered via Nav.AddPicker must be public and decorated with [Endpoint], [RequireAction], or [RequireRight].");
      }
    }

    var actions = ProbeActionsSync();
    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      if (!seenHandles.Add(entry.Handle))
      {
        throw new InvalidOperationException(
          $"Grid '{gridType.FullName}' declares duplicate action handle '{entry.Handle}'.");
      }

      entry.ValidateConfiguration(gridType, Feature);
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, formType, "Actions.UseActionsOf");
      var formEntityType = ParentScopeValidator.GetFormEntityType(formType);
      if (formEntityType != typeof(TEntity))
      {
        throw new InvalidOperationException(
          $"Grid '{gridType.FullName}' cannot call UseActionsOf<{formType.Name}>() because entity type '{formEntityType?.Name}' does not match '{typeof(TEntity).Name}'.");
      }

      if (catalog.CreateServiceInstance(formType, db, PlatformApp) is IFormService formProbe)
      {
        formProbe.ValidateInheritedGridActions(gridType);
      }
    }

    var hooks = GetHooks();
    if (hooks.DependentEntityTypes.Count > 0)
    {
      var dbType = ModuleCatalog.GetServiceDbContextType(gridType);
      if (dbType != null && dbType != typeof(PlatformDbContext))
      {
        var dbProps = dbType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var depType in hooks.DependentEntityTypes)
        {
          var expectedSetType = typeof(DbSet<>).MakeGenericType(depType);
          if (!dbProps.Any(p => p.PropertyType == expectedSetType))
          {
            throw new InvalidOperationException(
              $"Grid '{gridType.FullName}' registers PreventDeleteWhenUsedBy<{depType.Name}>(), but DbContext '{dbType.Name}' does not expose 'DbSet<{depType.Name}>'. Add 'public DbSet<{depType.Name}> ... => Set<{depType.Name}>();' to '{dbType.Name}'.");
          }
        }
      }
    }
  }
}
