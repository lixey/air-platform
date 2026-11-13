using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  private GridRestrictions<TEntity> BuildRestrictions()
  {
    Restrictions = new GridRestrictions<TEntity>();
    SetRestrictions();
    return Restrictions;
  }

  internal GridQuery<TEntity, TRow> GetQueryDeclaration()
  {
    if (State.CachedQuery is { } cached && cached.ParentService == ParentService && cached.ParentId == ParentId)
    {
      return Query = cached.Value;
    }

    Query = new GridQuery<TEntity, TRow>();
    SetQuery();
    State.CachedQuery = (ParentService, ParentId, Query);
    return Query;
  }

  internal Task<GridActions<TEntity>> BuildActionsInternalAsync(CancellationToken ct = default) =>
    BuildActionsAsync(ct);

  private async Task<GridActions<TEntity>> BuildActionsAsync(CancellationToken ct = default)
  {
    Actions = new GridActions<TEntity>();
    await SetActionsAsync(ct);
    return Actions;
  }

  private async Task<Columns<TEntity, TRow>> CreateColumnsAsync(CancellationToken ct = default)
  {
    Columns = new Columns<TEntity, TRow>();
    await SetColumnsAsync(ct);
    FinalizeColumns(Columns);
    return Columns;
  }

  private bool OverridesBaseMethod(string methodName) =>
    GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType != typeof(GridService<TEntity, TRow>);

  private Columns<TEntity, TRow> CreateColumnsSync()
  {
    Columns = new Columns<TEntity, TRow>();
    var cols = Columns;
    if (!OverridesBaseMethod(nameof(SetColumnsAsync)))
    {
      SetColumns();
      FinalizeColumns(cols);
      return cols;
    }

    try
    {
      SetColumnsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (cols.Count == 0)
      {
        try
        {
          SetColumns();
        }
        catch (ParentNotLoadedException)
        {
        }
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
    if (!OverridesBaseMethod(nameof(SetNavAsync)))
    {
      SetNav();
      FinalizeNav(nav, effectivePath);
      return nav;
    }

    try
    {
      SetNavAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (nav.Creates.Count == 0 && nav.PickerEntries.Count == 0 && nav.Start.Count == 0 && nav.Elements.Count == 0)
      {
        SetNav();
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

  private GridActions<TEntity> ProbeActions()
  {
    Actions = new GridActions<TEntity>();
    var actions = Actions;
    try
    {
      SetActionsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0 && actions.InheritedFormServiceTypes.Count == 0)
      {
        try
        {
          SetActions();
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
      if (actions.Entries.Count == 0 && actions.InheritedFormServiceTypes.Count == 0)
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
    foreach (var entry in ProbeActions().Entries)
    {
      entry.Ask?.BuildElements();
    }
  }

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    var cols = ProbeColumnsSafe();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    foreach (var entry in (await ProbeActionsAsync(ct)).Entries)
    {
      entry.Ask?.BuildElements();
    }
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
    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      gridType,
      typeof(GridService<,>),
      (nameof(SetColumns), nameof(SetColumnsAsync)),
      (nameof(SetNav), nameof(SetNavAsync)),
      (nameof(SetActions), nameof(SetActionsAsync)));

    _ = GetParent();
    var cols = ProbeColumnsSafe();
    foreach (var col in cols.List)
    {
      if (col.PickerTargetMethod != null && !ServiceEndpointsExtensions.IsEndpointMethod(col.PickerTargetMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{gridType.FullName}.{col.PickerTargetMethod.Name}' registered via PickedBy on '{col.Path}' must be public and decorated with [Endpoint], [RequireAction], or [RequireRight].");
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

    var actions = ProbeActions();
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
