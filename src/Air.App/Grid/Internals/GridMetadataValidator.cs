using Platform.App.Host.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridMetadataValidator
{
  public static IReadOnlyList<string> GetRoutableActionMethods<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    ModuleCatalog catalog)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var actions = grid.ProbeActions();
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

    AppServiceRouteMapper.AddFeatureCustomRightRoutes(routes, grid.Feature);
    return [.. routes];
  }

  public static void RegisterMetadata<TEntity, TRow>(GridService<TEntity, TRow> grid)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var cols = grid.ProbeColumnsSafe();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    foreach (var entry in grid.ProbeActions().Entries)
    {
      entry.Ask?.BuildElements();
    }
  }

  public static async Task RegisterMetadataAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var cols = grid.ProbeColumnsSafe();
    (cols.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    var actions = new GridService<TEntity, TRow>.Actions();
    try
    {
      await grid.InvokeSetActionsAsync(actions, ct);
    }
    catch (Exception ex) when (grid.DbContext == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          grid.InvokeSetActions(actions);
        }
        catch (ParentNotLoadedException)
        {
        }
      }
    }

    foreach (var entry in actions.Entries)
    {
      entry.Ask?.BuildElements();
    }
  }

  public static void ValidateConfiguration<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    ModuleCatalog catalog)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var gridType = grid.GetType();
    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      gridType,
      typeof(GridService<,>),
      ("SetActions", "SetActionsAsync"));
    ServiceOverrideValidator.EnsureSingleColumnOverride(gridType, typeof(GridService<,>));

    _ = grid.GetParentDeclaration();
    var cols = grid.ProbeColumnsSafe();
    foreach (var col in cols.List)
    {
      if (col.PickerTargetMethod != null && !ServiceEndpointsExtensions.IsCallableServiceAction(col.PickerTargetMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{gridType.FullName}.{col.PickerTargetMethod.Name}' registered via PickedBy on '{col.Path}' must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }

    var nav = grid.ProbeNavSafe();
    foreach (var (pickerSourceType, pickerMethod, _) in nav.PickerEntries)
    {
      ReferenceLookupAllowlist.RegisterReferrer(pickerSourceType, gridType);
      if (!ServiceEndpointsExtensions.IsCallableServiceAction(pickerMethod))
      {
        throw new InvalidOperationException(
          $"Grid picker method '{gridType.FullName}.{pickerMethod.Name}' registered via Nav.AddPicker must be public and decorated with [RequireAction], [RequireRight], or [ServiceAction].");
      }
    }

    var actions = new GridService<TEntity, TRow>.Actions();
    try
    {
      grid.InvokeSetActionsAsync(actions, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (grid.DbContext == null || ex is ParentNotLoadedException)
    {
      actions = new GridService<TEntity, TRow>.Actions();
      try
      {
        grid.InvokeSetActions(actions);
      }
      catch (ParentNotLoadedException)
      {
      }
    }

    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      if (!seenHandles.Add(entry.Handle))
      {
        throw new InvalidOperationException(
          $"Grid '{gridType.FullName}' declares duplicate action handle '{entry.Handle}'.");
      }

      entry.ValidateConfiguration(gridType, grid.Feature);
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

      if (catalog.CreateServiceInstance(formType, grid.DbContext, grid.PlatformApp) is IFormService formProbe)
      {
        formProbe.ValidateInheritedGridActions(gridType);
      }
    }

    var hooks = grid.GetHooksDeclaration();
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
