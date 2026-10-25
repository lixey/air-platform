namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  private async Task ResolveTimeZoneInheritLabelsAsync(Elements elements, TEntity? entity, CancellationToken ct)
  {
    var tzModels = elements.List.Select(e => e.Model).OfType<FormTimeZoneModel>().ToList();
    if (tzModels.Count == 0)
    {
      return;
    }

    var isTenantForm = typeof(BaseAppTenant).IsAssignableFrom(typeof(TEntity));
    var startTenantId = isTenantForm
      ? entity is BaseAppTenant tenantEntity ? tenantEntity.ParentId : ParentId
      : entity is IHasTenant hasTenant && hasTenant.TenantId > 0
        ? hasTenant.TenantId
        : db.TenantContext.TenantId;
    string? resolvedZone = null;
    if (startTenantId is > 0)
    {
      var tenantClrType = db.Model.GetEntityTypes()
        .Select(t => t.ClrType)
        .FirstOrDefault(t => typeof(BaseAppTenant).IsAssignableFrom(t) && !t.IsAbstract);

      if (tenantClrType != null)
      {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(tenantClrType);
        if (setMethod.Invoke(db, null) is IQueryable<BaseAppTenant> tenantQuery)
        {
          var currentId = startTenantId;
          var visited = new HashSet<long>();
          while (currentId is > 0 && visited.Add(currentId.Value))
          {
            var row = await tenantQuery.IgnoreQueryFilters()
              .AsNoTracking()
              .Where(t => t.Id == currentId.Value)
              .Select(t => new { t.ParentId, t.TimeZone })
              .FirstOrDefaultAsync(ct);
            if (row == null)
            {
              break;
            }

            if (!string.IsNullOrWhiteSpace(row.TimeZone))
            {
              resolvedZone = row.TimeZone;
              break;
            }

            currentId = row.ParentId;
          }
        }
      }
    }

    var inheritLabel = !string.IsNullOrWhiteSpace(resolvedZone)
      ? (isTenantForm
          ? L["TimeZoneAsParentOrganisation", resolvedZone]
          : L["TimeZoneAsOrganisation", resolvedZone])
      : L["TimeZoneByBrowser"];

    foreach (var tz in tzModels)
    {
      tz.Inherit = inheritLabel;
    }
  }

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    CancellationToken ct = default)
  {
    if (entity == null)
    {
      return;
    }

    if (_lastElements == null)
    {
      var elements = new Elements();
      await SetElementsAsync(elements, ct);
      LocalizeAndResolveSelectElements(elements);
      _lastElements = elements;
    }

    await PopulateReferencesCoreAsync(dict, entity, _lastElements.List, ct);
  }

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    ScreenDto screen,
    CancellationToken ct = default)
  {
    if (entity == null || screen.Layout?.Root == null)
    {
      return;
    }

    var formModel = FindFormModel(screen.Layout.Root);
    if (formModel == null)
    {
      return;
    }

    await PopulateReferencesCoreAsync(dict, entity, formModel.Elements, ct);
  }

  private async Task PopulateReferencesCoreAsync(
    Dictionary<string, object?> dict,
    object entity,
    IEnumerable<FormElementDto> elements,
    CancellationToken ct)
  {
    var entityType = entity.GetType();
    var refPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.ReferencePath))
      {
        continue;
      }

      refPaths.Add(el.ReferencePath);

      var selectModel = el.Model as FormSelectModel;
      var labelPaths = selectModel?.ReferenceLabelPaths;

      if (selectModel?.IsMultiple == true)
      {
        var keys = new List<long>();
        if (dict.TryGetValue(el.Path, out var multiRaw) && multiRaw != null)
        {
          if (multiRaw is JsonElement jeArr && jeArr.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in jeArr.EnumerateArray())
            {
              if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var jeId) && jeId > 0)
              {
                keys.Add(jeId);
              }
            }
          }
          else if (multiRaw is IEnumerable enumerable and not string)
          {
            foreach (var item in enumerable)
            {
              if (item is long l && l > 0)
              {
                keys.Add(l);
              }
              else if (item is int i && i > 0)
              {
                keys.Add(i);
              }
              else if (item != null && long.TryParse(item.ToString(), out var parsed) && parsed > 0)
              {
                keys.Add(parsed);
              }
            }
          }
        }

        var refs = new List<Reference>(keys.Count);
        if (keys.Count > 0 && !string.IsNullOrEmpty(selectModel.ReferenceService))
        {
          var refServiceType = el.ReferenceServiceType
            ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
            ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType;
          var refEntityType = ModuleCatalog.GetServiceEntityType(refServiceType);
          var targetDb = refEntityType != null && db.Model.FindEntityType(refEntityType) != null
            ? db
            : (refServiceType != null ? (ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, PlatformApp) as IGridService)?.DbContext : null) ?? db;

          var loadedById = refEntityType != null
            ? await LoadEntitiesByIdsAsync(targetDb, refEntityType, keys, ct)
            : [];

          foreach (var k in keys)
          {
            var itemCaption = loadedById.TryGetValue(k, out var loaded)
              ? ReadDisplayCaption(loaded, labelPaths)
              : null;
            refs.Add(new Reference { Id = k, Name = !string.IsNullOrEmpty(itemCaption) ? itemCaption : $"#{k}" });
          }
        }

        dict[el.ReferencePath] = refs;
        continue;
      }

      long? key = null;
      if (dict.TryGetValue(el.Path, out var rawVal) && rawVal != null)
      {
        if (rawVal is JsonElement je)
        {
          if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jeId))
          {
            key = jeId;
          }
          else if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var jeParsed))
          {
            key = jeParsed;
          }
        }
        else if (rawVal is long l)
        {
          key = l;
        }
        else if (rawVal is int i)
        {
          key = i;
        }
        else if (long.TryParse(rawVal.ToString(), out var parsedId))
        {
          key = parsedId;
        }
      }

      if (key == null || key <= 0)
      {
        dict[el.ReferencePath] = null;
        continue;
      }

      var navObj = PropertyAccessorCache.GetValue(entity, el.ReferencePath);
      if (navObj == null)
      {
        var candidateName = el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
          ? el.Path[..^2]
          : el.Path;
        if (PropertyAccessorCache.GetProperty(entityType, candidateName) is not null)
        {
          navObj = PropertyAccessorCache.GetValue(entity, candidateName);
        }
      }

      var refName = ReadDisplayCaption(navObj, labelPaths);

      if (string.IsNullOrEmpty(refName) && selectModel != null && !string.IsNullOrEmpty(selectModel.ReferenceService))
      {
        var refServiceType = ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
          ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType;
        var refEntityType = ModuleCatalog.GetServiceEntityType(refServiceType);
        if (refServiceType != null && refEntityType != null)
        {
          try
          {
            var targetDb = db.Model.FindEntityType(refEntityType) != null
              ? db
              : (ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, PlatformApp) as IGridService)?.DbContext ?? db;
            var loaded = await targetDb.FindAsync(refEntityType, [key.Value], ct);
            refName = ReadDisplayCaption(loaded, labelPaths);
          }
          catch { }
        }
      }

      dict[el.ReferencePath] = new Reference
      {
        Id = key.Value,
        Name = !string.IsNullOrEmpty(refName) ? refName : $"#{key.Value}"
      };
    }

    var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements)
    {
      if (!string.IsNullOrEmpty(el.Path) && !el.IsSecret && !string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase))
      {
        allowedKeys.Add(el.Path);
      }

      if (!string.IsNullOrEmpty(el.ReferencePath))
      {
        allowedKeys.Add(el.ReferencePath);
      }
    }

    foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (Attribute.IsDefined(prop, typeof(SensitiveAttribute)))
      {
        allowedKeys.Remove(JsonNamingPolicy.CamelCase.ConvertName(prop.Name));
      }
    }

    if (allowedKeys.Count > 0)
    {
      foreach (var key in dict.Keys.ToList())
      {
        if (!allowedKeys.Contains(key))
        {
          dict.Remove(key);
        }
      }
    }
  }

  private static readonly MethodInfo LoadEntitiesByIdsGenericMethod =
    typeof(FormService<TEntity, TInput>).GetMethod(nameof(LoadEntitiesByIdsCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static async Task<Dictionary<long, object>> LoadEntitiesByIdsAsync(
    DbContext targetDb,
    Type entityType,
    IReadOnlyList<long> ids,
    CancellationToken ct)
  {
    if (ids.Count == 0 || !typeof(IEntity).IsAssignableFrom(entityType))
    {
      return [];
    }

    try
    {
      var task = (Task<Dictionary<long, object>>)LoadEntitiesByIdsGenericMethod
        .MakeGenericMethod(entityType)
        .Invoke(null, [targetDb, ids, ct])!;
      return await task;
    }
    catch
    {
      return [];
    }
  }

  private static async Task<Dictionary<long, object>> LoadEntitiesByIdsCoreAsync<TRefEntity>(
    DbContext targetDb,
    IReadOnlyList<long> ids,
    CancellationToken ct)
    where TRefEntity : class, IEntity
  {
    var rows = await targetDb.Set<TRefEntity>()
      .AsNoTracking()
      .Where(e => ids.Contains(e.Id))
      .ToListAsync(ct);
    return rows.ToDictionary(r => r.Id, r => (object)r);
  }

  private static string? ReadDisplayCaption(object? target, IReadOnlyList<string>? labelPaths = null)
  {
    if (target == null)
    {
      return null;
    }

    if (labelPaths is { Count: > 0 })
    {
      var parts = new List<string>(labelPaths.Count);
      foreach (var path in labelPaths)
      {
        var rawPart = PropertyAccessorCache.GetValue(target, path);
        if (rawPart is null or Reference or IEntity)
        {
          continue;
        }

        var partVal = rawPart.ToString();
        if (!string.IsNullOrWhiteSpace(partVal))
        {
          parts.Add(partVal);
        }
      }

      if (parts.Count > 0)
      {
        return string.Join(" ", parts);
      }
    }

    var val = PropertyAccessorCache.GetValue(target, "Name")
      ?? PropertyAccessorCache.GetValue(target, "Title")
      ?? PropertyAccessorCache.GetValue(target, "Number")
      ?? PropertyAccessorCache.GetValue(target, "Login");
    return val?.ToString();
  }

  private static FormModel? FindFormModel(PanelNodeDto node)
  {
    if (node.Model is FormModel fm)
    {
      return fm;
    }

    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        var res = FindFormModel(child);
        if (res != null)
        {
          return res;
        }
      }
    }

    return null;
  }
}
